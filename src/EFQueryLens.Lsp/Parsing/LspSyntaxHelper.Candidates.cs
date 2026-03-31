using System.Collections.Generic;
using System.Linq;
using EFQueryLens.Core.Contracts;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

public static partial class LspSyntaxHelper
{
    public static bool IsLikelyQueryPreviewCandidate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        ExpressionSyntax parsed;
        try
        {
            parsed = SyntaxFactory.ParseExpression(expression);
        }
        catch
        {
            return false;
        }

        if (parsed is InvocationExpressionSyntax invocation)
        {
            var hasKnownQueryMethods = GetInvocationChainMethodNames(invocation)
                .Any(name => TerminalMethods.Contains(name) || QueryChainMethods.Contains(name));
            if (hasKnownQueryMethods)
            {
                var rootName = TryExtractRootContextVariable(invocation);
                if (!LooksLikeDbContextRoot(rootName) && LooksLikeStaticTypeRoot(rootName))
                {
                    return false;
                }

                return true;
            }

            var invocationRootName = TryExtractRootContextVariable(invocation);
            return LooksLikeDbContextRoot(invocationRootName);
        }

        if (parsed is MemberAccessExpressionSyntax memberAccess)
        {
            var rootName = TryExtractRootContextVariable(memberAccess);
            return LooksLikeDbContextRoot(rootName);
        }

        if (parsed is QueryExpressionSyntax queryExpression)
        {
            var rootName = TryExtractRootContextVariable(queryExpression);
            return LooksLikeDbContextRoot(rootName);
        }

        return false;
    }

    public static bool IsLikelyDbContextRootIdentifier(string? rootName)
    {
        return LooksLikeDbContextRoot(rootName);
    }

    public static IReadOnlyList<LinqChainInfo> FindAllLinqChains(string sourceText)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetRoot();

        var results = new List<LinqChainInfo>();
        // Deduplicate by containing statement: for each statement, keep only the
        // single largest outermost invocation chain. This prevents one big fluent
        // chain (Include->ThenInclude->Include->ThenInclude...) from producing multiple
        // badges because each ThenInclude subtree resolves to a different "outermost".
        var bestPerStatement = new Dictionary<int, (InvocationExpressionSyntax Invocation, int Span)>(capacity: 32);

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            // Ignore nested queries inside lambda selectors/predicates. The outer query
            // already captures the SQL that EF Core will generate.
            if (IsInsideLambda(invocation))
            {
                continue;
            }

            var outermostInvocation = GetOutermostInvocationChain(invocation);
            if (!IsLikelyQueryChain(outermostInvocation))
            {
                continue;
            }

            // Key by the start position of the containing statement so we get one
            // chain per statement, keeping the one with the largest span.
            var containingStmt = outermostInvocation.Ancestors()
                .FirstOrDefault(a => a is StatementSyntax) as StatementSyntax;
            var stmtKey = containingStmt?.Span.Start ?? outermostInvocation.Span.Start;
            var invocationSpan = outermostInvocation.Span.Length;

            if (bestPerStatement.TryGetValue(stmtKey, out var existing))
            {
                if (invocationSpan <= existing.Span)
                    continue;
            }

            bestPerStatement[stmtKey] = (outermostInvocation, invocationSpan);
        }

        foreach (var (_, (outermostInvocation, _)) in bestPerStatement)
        {
            // Pass the full expression including any terminal call (Count, ToList, etc.)
            // so the engine sees exactly what the app executes and produces accurate SQL.
            ExpressionSyntax targetExpression = outermostInvocation;
            if (targetExpression is null)
            {
                continue;
            }

            // If the outermost chain is chained on the result of an await expression
            // (e.g. "(await query.ToListAsync()).ToList()"), strip the outer in-memory
            // part and keep only the awaited EF query.  The runner template already
            // handles Task<T> via UnwrapTask; keeping the await would cause CS4032
            // in the generated synchronous scaffold.
            targetExpression = StripOuterAwaitChain(targetExpression);

            targetExpression = TryInlineLocalQueryRoot(targetExpression, outermostInvocation);
            targetExpression = StripTransparentQueryableCasts(targetExpression);

            var contextVariableName = TryExtractRootContextVariable(targetExpression)
                ?? targetExpression.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Select(i => i.Identifier.Text)
                    .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(contextVariableName))
            {
                contextVariableName = TryExtractRootContextVariable(outermostInvocation)
                    ?? outermostInvocation.DescendantNodes()
                        .OfType<IdentifierNameSyntax>()
                        .Select(i => i.Identifier.Text)
                        .FirstOrDefault();
            }

            if (string.IsNullOrWhiteSpace(contextVariableName))
            {
                continue;
            }

            // Reject in-memory LINQ on plain objects (e.g. someDto.Items.Select(...).ToList()).
            // Keep only chains that are clearly EF: rooted at a recognisable DbContext variable
            // OR that use at least one EF-Core-specific method (Include, AsNoTracking, etc.).
            var hasEfSpecificMethod = GetInvocationChainMethodNames(outermostInvocation)
                .Any(m => EfSpecificMethods.Contains(m));
            if (!LooksLikeDbContextRoot(contextVariableName) && !hasEfSpecificMethod)
            {
                continue;
            }

            if (!TryExtractFirstMemberAfterRoot(targetExpression, out var dbSetMemberName)
                || string.IsNullOrWhiteSpace(dbSetMemberName))
            {
                if (!TryExtractFirstMemberAfterRoot(outermostInvocation, out dbSetMemberName)
                    || string.IsNullOrWhiteSpace(dbSetMemberName))
                {
                    // Keep anchor discovery resilient for terminal/complex chains even
                    // when DbSet member extraction is inconclusive.
                    dbSetMemberName = contextVariableName;
                }
            }

            // Use the first token of the invocation chain as the anchor so preview navigation
            // lands near the LINQ root instead of the trailing ")" in long fluent chains.
            var expressionText = targetExpression.ToString();
            if (string.IsNullOrWhiteSpace(expressionText))
            {
                expressionText = outermostInvocation.ToString();
            }

            var firstToken = outermostInvocation.GetFirstToken();
            var anchorLineSpan = tree.GetLineSpan(firstToken.Span);
            var anchorStart = anchorLineSpan.StartLinePosition;
            var anchorEnd = anchorLineSpan.EndLinePosition;

            // Expression span drives the END of the hover range — prevents bleeding into if/while bodies.
            // Using outermostInvocation.Span ensures hovers inside an if-body don't match the condition chain.
            var expressionLineSpan = tree.GetLineSpan(outermostInvocation.Span);
            var expressionStart = expressionLineSpan.StartLinePosition;
            var expressionEnd = expressionLineSpan.EndLinePosition;

            // Containing statement span drives the START of the hover range.
            // This covers the full declaration line: "var x = await expr" so hovering on
            // "var", "x", "=", or "await" (before the expression token) still matches the chain.
            var containingStatement = outermostInvocation.Ancestors()
                .FirstOrDefault(a => a is StatementSyntax) as StatementSyntax;
            var statementLineSpan = containingStatement != null
                ? tree.GetLineSpan(containingStatement.Span)
                : expressionLineSpan;
            var statementStart = statementLineSpan.StartLinePosition;

            // Badge placement: CodeLens badge sits above the start of the containing statement.
            var badgeLine = statementStart.Line;
            var badgeCharacter = 0;

            results.Add(new LinqChainInfo(
                expressionText,
                contextVariableName,
                dbSetMemberName,
                anchorStart.Line,
                anchorStart.Character,
                anchorEnd.Line,
                anchorEnd.Character,
                badgeLine,
                badgeCharacter,
                statementStart.Line,    // wider start: "var x = await" included
                statementStart.Character,
                expressionEnd.Line,     // tight end: stops at expression, not if-body
                expressionEnd.Character));
        }

        foreach (var queryExpression in root.DescendantNodes().OfType<QueryExpressionSyntax>())
        {
            // Statement already has a terminal/member-invocation candidate; keep the
            // existing outermost invocation selection to avoid duplicate badges.
            var containingStmt = queryExpression.Ancestors().OfType<StatementSyntax>().FirstOrDefault();
            var stmtKey = containingStmt?.Span.Start ?? queryExpression.Span.Start;
            if (bestPerStatement.ContainsKey(stmtKey))
            {
                continue;
            }

            if (IsInsideLambda(queryExpression))
            {
                continue;
            }

            var targetExpression = StripTransparentQueryableCasts(queryExpression);
            var contextVariableName = TryExtractRootContextVariable(targetExpression)
                ?? targetExpression.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Select(i => i.Identifier.Text)
                    .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(contextVariableName)
                || !LooksLikeDbContextRoot(contextVariableName))
            {
                continue;
            }

            if (!TryExtractFirstMemberAfterRoot(targetExpression, out var dbSetMemberName)
                || string.IsNullOrWhiteSpace(dbSetMemberName))
            {
                dbSetMemberName = contextVariableName;
            }

            var expressionText = targetExpression.ToString();
            var firstToken = queryExpression.GetFirstToken();
            var anchorLineSpan = tree.GetLineSpan(firstToken.Span);
            var anchorStart = anchorLineSpan.StartLinePosition;
            var anchorEnd = anchorLineSpan.EndLinePosition;

            var expressionLineSpan = tree.GetLineSpan(queryExpression.Span);
            var expressionEnd = expressionLineSpan.EndLinePosition;

            var statementLineSpan = containingStmt != null
                ? tree.GetLineSpan(containingStmt.Span)
                : expressionLineSpan;
            var statementStart = statementLineSpan.StartLinePosition;

            results.Add(new LinqChainInfo(
                expressionText,
                contextVariableName,
                dbSetMemberName,
                anchorStart.Line,
                anchorStart.Character,
                anchorEnd.Line,
                anchorEnd.Character,
                statementStart.Line,
                0,
                statementStart.Line,
                statementStart.Character,
                expressionEnd.Line,
                expressionEnd.Character));
        }

        return results
            .OrderBy(r => r.Line)
            .ThenBy(r => r.Character)
            .ToArray();
    }

    /// <summary>
    /// Scans <paramref name="sourceText"/> for a field, local variable, or parameter declaration
    /// whose name matches <paramref name="contextVariableName"/> and returns the declared type
    /// name string - suitable for populating <c>TranslationRequest.DbContextTypeName</c> to
    /// disambiguate when multiple DbContext types exist in the host assembly.
    ///
    /// Returns <c>null</c> when the variable cannot be found or its type cannot be determined
    /// syntactically (e.g. <c>var</c> with a complex initializer).
    /// </summary>
    internal static string? TryResolveDbContextTypeName(string sourceText, string contextVariableName)
    {
        if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(contextVariableName))
            return null;

        try
        {
            var root = CSharpSyntaxTree.ParseText(sourceText).GetRoot();

            string? resolved = null;

            // Fields: private readonly SlaPlusDbContext _db;
            foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            {
                if (field.Declaration.Variables.Any(v =>
                        v.Identifier.ValueText.Equals(contextVariableName, StringComparison.Ordinal)))
                {
                    resolved = field.Declaration.Type.ToString();
                    break;
                }
            }

            // Locals: var db = ...; SlaPlusDbContext db = ...;
            if (resolved is null)
            {
                foreach (var local in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
                {
                    if (local.Declaration.Variables.Any(v =>
                            v.Identifier.ValueText.Equals(contextVariableName, StringComparison.Ordinal)))
                    {
                        resolved = local.Declaration.Type.ToString();
                        break;
                    }
                }
            }

            // Parameters: (SlaPlusDbContext db) or injected via ctor
            if (resolved is null)
            {
                foreach (var parameter in root.DescendantNodes().OfType<ParameterSyntax>())
                {
                    if (parameter.Identifier.ValueText.Equals(contextVariableName, StringComparison.Ordinal)
                        && parameter.Type is not null)
                    {
                        resolved = parameter.Type.ToString();
                        break;
                    }
                }
            }

            return resolved is not null ? NormalizeDbContextTypeName(resolved) : null;
        }
        catch
        {
            // Best-effort - never propagate to caller.
        }

        return null;
    }

    /// <summary>
    /// Normalises a syntactically-resolved type name for use as a DbContext disambiguator.
    /// Strips nullable-reference-type annotations (<c>?</c>) - they have no CLR distinction.
    /// </summary>
    private static string NormalizeDbContextTypeName(string typeName) => typeName.TrimEnd('?');

    internal static DbContextResolutionSnapshot? BuildDbContextResolutionSnapshot(
        string sourceText,
        string contextVariableName,
        IReadOnlyList<string>? factoryCandidateTypeNames)
    {
        var declaredTypeName = TryResolveDbContextTypeName(sourceText, contextVariableName);
        var normalizedFactoryCandidates = (factoryCandidateTypeNames ?? [])
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(NormalizeDbContextTypeName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var factoryTypeName = normalizedFactoryCandidates.Length == 1
            ? normalizedFactoryCandidates[0]
            : null;

        if (declaredTypeName is null && factoryTypeName is null && normalizedFactoryCandidates.Length == 0)
            return null;

        return new DbContextResolutionSnapshot
        {
            DeclaredTypeName = declaredTypeName,
            FactoryTypeName = factoryTypeName,
            FactoryCandidateTypeNames = normalizedFactoryCandidates,
            ResolutionSource = BuildResolutionSource(declaredTypeName, factoryTypeName, normalizedFactoryCandidates),
            Confidence = ComputeResolutionConfidence(declaredTypeName, factoryTypeName, normalizedFactoryCandidates),
        };
    }

    internal static string? GetPreferredDbContextTypeName(DbContextResolutionSnapshot? snapshot)
    {
        if (snapshot is null)
            return null;

        if (!string.IsNullOrWhiteSpace(snapshot.DeclaredTypeName))
            return snapshot.DeclaredTypeName;

        if (!string.IsNullOrWhiteSpace(snapshot.FactoryTypeName))
            return snapshot.FactoryTypeName;

        return snapshot.FactoryCandidateTypeNames.Count == 1
            ? snapshot.FactoryCandidateTypeNames[0]
            : null;
    }

    internal static string GetDbContextResolutionCacheToken(DbContextResolutionSnapshot? snapshot)
    {
        if (snapshot is null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(snapshot.DeclaredTypeName))
            return snapshot.DeclaredTypeName;

        if (!string.IsNullOrWhiteSpace(snapshot.FactoryTypeName))
            return snapshot.FactoryTypeName;

        if (snapshot.FactoryCandidateTypeNames.Count == 0)
            return string.Empty;

        return string.Join(";", snapshot.FactoryCandidateTypeNames.Order(StringComparer.Ordinal));
    }

    private static string BuildResolutionSource(
        string? declaredTypeName,
        string? factoryTypeName,
        IReadOnlyList<string> factoryCandidateTypeNames)
    {
        if (!string.IsNullOrWhiteSpace(declaredTypeName) && !string.IsNullOrWhiteSpace(factoryTypeName))
        {
            return string.Equals(declaredTypeName, factoryTypeName, StringComparison.Ordinal)
                ? "declared+factory"
                : "declared+factory-mismatch";
        }

        if (!string.IsNullOrWhiteSpace(declaredTypeName) && factoryCandidateTypeNames.Count > 1)
            return "declared+factory-candidates";

        if (!string.IsNullOrWhiteSpace(declaredTypeName))
            return "declared";

        if (!string.IsNullOrWhiteSpace(factoryTypeName))
            return "factory";

        return factoryCandidateTypeNames.Count > 1 ? "factory-candidates" : "unknown";
    }

    private static double ComputeResolutionConfidence(
        string? declaredTypeName,
        string? factoryTypeName,
        IReadOnlyList<string> factoryCandidateTypeNames)
    {
        if (!string.IsNullOrWhiteSpace(declaredTypeName) && !string.IsNullOrWhiteSpace(factoryTypeName))
        {
            return string.Equals(declaredTypeName, factoryTypeName, StringComparison.Ordinal)
                ? 1.0
                : 0.4;
        }

        if (!string.IsNullOrWhiteSpace(declaredTypeName) && factoryCandidateTypeNames.Count > 1)
            return factoryCandidateTypeNames.Contains(declaredTypeName, StringComparer.Ordinal) ? 0.9 : 0.6;

        if (!string.IsNullOrWhiteSpace(declaredTypeName))
            return 0.9;

        if (!string.IsNullOrWhiteSpace(factoryTypeName))
            return 0.85;

        return factoryCandidateTypeNames.Count > 1 ? 0.5 : 0.0;
    }
}
