using System.Collections.Generic;
using System.Linq;
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
            var targetExpression = StripTerminalInvocation(outermostInvocation) ?? outermostInvocation;
            if (targetExpression is null)
            {
                continue;
            }

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

            // Containing statement: for badge (line above) and for hover binding (full statement span).
            var containingStatement = outermostInvocation.Ancestors().FirstOrDefault(a => a is StatementSyntax) as StatementSyntax;
            var statementSpan = containingStatement != null ? tree.GetLineSpan(containingStatement.Span) : anchorLineSpan;
            var statementStart = statementSpan.StartLinePosition;
            var statementEnd = statementSpan.EndLinePosition;
            var statementFirstLine = statementStart.Line;

            // Badge at statementFirstLine: VS Code CodeLens appears above the line it is placed on,
            // so placing it at the first line of the statement puts it visually on top of that line.
            var badgeLine = statementFirstLine;
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
                statementStart.Line,
                statementStart.Character,
                statementEnd.Line,
                statementEnd.Character));
        }

        return results
            .OrderBy(r => r.Line)
            .ThenBy(r => r.Character)
            .ToArray();
    }
}
