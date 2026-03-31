using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

public static partial class LspSyntaxHelper
{
    public static string? TryExtractLinqExpression(string sourceText, int line, int character,
        out string? contextVariableName,
        ProjectSourceIndex? sourceIndex = null)
    {
        contextVariableName = null;

        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetRoot();

        var textLines = sourceText.Split('\n');
        if (line >= textLines.Length) return null;

        var textLine = textLines[line];
        if (character > textLine.Length) return null;

        // Find the absolute position from Line/Char
        var position = tree.GetText().Lines[line].Start + character;

        // Find the node at the cursor position
        var node = root.FindToken(position).Parent;

        // Walk up until we find an InvocationExpression (like .Where() or .ToList()),
        // a MemberAccessExpression (like db.Orders), or a query-expression root
        // (from ... in ... select ...).
        var invocation = node?.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        var memberAccess = node?.FirstAncestorOrSelf<MemberAccessExpressionSyntax>();
        var queryExpression = node?.FirstAncestorOrSelf<QueryExpressionSyntax>();

        ExpressionSyntax? targetExpression = invocation
            ?? (ExpressionSyntax?)memberAccess
            ?? queryExpression;

        if (targetExpression == null)
            return null;

        // Walk to the topmost invocation/member access, including any terminal call
        // (Count, ToList, FirstOrDefaultAsync, ExecuteDeleteAsync, etc.) so the engine
        // receives the exact expression the app runs and generates the real SQL.
        while (targetExpression.Parent is MemberAccessExpressionSyntax or InvocationExpressionSyntax)
        {
            targetExpression = (ExpressionSyntax)targetExpression.Parent;
        }

        // Guard: reject expressions that are not LINQ query chains.
        // Without this, hovering inside a lambda argument of a non-LINQ method call
        // (e.g. "x => new Dto{...}" passed to GetFooAsync(id, x => new Dto{...}, ct))
        // causes the entire call site to be extracted as the LINQ expression, with the
        // method name mis-identified as the DbContext variable name. The engine then
        // declares a variable using that name and later tries to invoke it as a method,
        // producing CS0149: Method name expected.
        // GetInvocationChainMethodNames only yields for member-access chains (a.b.c()),
        // so a bare call like GetFooAsync(...) yields nothing → IsLikelyQueryChain = false.
        if (targetExpression is InvocationExpressionSyntax finalInvocation
            && !IsLikelyQueryChain(finalInvocation))
        {
            if (TryExtractFromExpressionParameterHelperCall(
                    root,
                    finalInvocation,
                    position,
                    out var synthesizedExpression,
                    out var synthesizedContextVariableName,
                        sourceIndex))
            {
                contextVariableName = synthesizedContextVariableName;
                return synthesizedExpression;
            }

            // The cursor is inside a nested call (e.g. a predicate method inside a lambda
            // argument: "w.IsNotDeleted()" inside ".Where(w => w.IsNotDeleted())").
            // Walk up through ancestors to find a containing LINQ query chain — this
            // handles hovering on any token within a .Where(…) or .Select(…) lambda
            // in Visual Studio / Rider, where the QuickInfo trigger fires on the exact
            // token under the cursor rather than the method name.
            var outerChain = node?.AncestorsAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Select(GetOutermostInvocationChain)
                .FirstOrDefault(IsLikelyQueryChain);

            if (outerChain is null)
            {
                return null;
            }

            targetExpression = outerChain;
        }

        // If the outermost chain is chained on the result of an await expression
        // (e.g. "(await query.ToListAsync()).ToList()"), strip the outer in-memory
        // part and keep only the awaited EF query.  The runner template already
        // handles Task<T> via UnwrapTask; keeping the await would cause CS4032
        // in the generated synchronous scaffold.
        targetExpression = StripOuterAwaitChain(targetExpression);

        // Inline local IQueryable variables for non-terminal chains too, so
        // expressions like auditTrailQuery.ApplyPaging(...).ToListAsync(...) are
        // rooted back to dbContext.* and keep DbContext discovery deterministic.
        if (invocation is not null)
        {
            targetExpression = TryInlineLocalQueryRoot(targetExpression, invocation);
        }

        targetExpression = StripTransparentQueryableCasts(targetExpression);

        // Identify the root variable from the left-most chain segment.
        // Using DescendantNodes().FirstOrDefault() can pick lambda identifiers
        // (e.g. "s") depending on cursor position and trivia layout.
        contextVariableName = TryExtractRootContextVariable(targetExpression)
            ?? targetExpression.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Select(i => i.Identifier.Text)
                .FirstOrDefault();

        return targetExpression.ToString();
    }

    public static SourceUsingContext ExtractUsingContext(string sourceText)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetRoot();

        var imports = new List<string>();
        var importSet = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var staticTypes = new List<string>();
        var staticSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            var name = usingDirective.Name?.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (usingDirective.Alias is { Name.Identifier.ValueText: { Length: > 0 } aliasName })
            {
                aliases[aliasName] = name;
                continue;
            }

            if (!usingDirective.StaticKeyword.IsKind(SyntaxKind.None))
            {
                if (staticSet.Add(name))
                {
                    staticTypes.Add(name);
                }

                continue;
            }

            if (importSet.Add(name))
            {
                imports.Add(name);
            }
        }

        // Add namespaces declared in the file itself. Code inside a namespace can
        // use extension methods from that same namespace without an explicit using,
        // but QueryLens compiles generated snippets in the global namespace, so we
        // need to import these explicitly to preserve behavior.
        foreach (var namespaceDecl in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
        {
            var ns = namespaceDecl.Name.ToString();
            if (string.IsNullOrWhiteSpace(ns))
            {
                continue;
            }

            if (importSet.Add(ns))
            {
                imports.Add(ns);
            }
        }

        return new SourceUsingContext(imports, aliases, staticTypes);
    }

    private static string? TryExtractRootContextVariable(ExpressionSyntax expression)
    {
        var current = expression;
        string? lastMemberName = null;

        while (true)
        {
            switch (current)
            {
                case InvocationExpressionSyntax invocation:
                    current = invocation.Expression;
                    continue;

                case MemberAccessExpressionSyntax memberAccess:
                    lastMemberName = memberAccess.Name.Identifier.Text;
                    current = memberAccess.Expression;
                    continue;

                case ParenthesizedExpressionSyntax parenthesized:
                    current = parenthesized.Expression;
                    continue;

                case QueryExpressionSyntax queryExpression:
                    current = queryExpression.FromClause.Expression;
                    continue;

                case IdentifierNameSyntax identifier:
                    return identifier.Identifier.Text;

                case ThisExpressionSyntax:
                    return lastMemberName;

                default:
                    return lastMemberName;
            }
        }
    }

    private static bool TryExtractFirstMemberAfterRoot(
        ExpressionSyntax expression,
        out string memberName)
    {
        memberName = string.Empty;
        var current = expression;

        while (true)
        {
            switch (current)
            {
                case InvocationExpressionSyntax invocation
                    when invocation.Expression is MemberAccessExpressionSyntax memberAccess:
                    if (memberAccess.Expression is IdentifierNameSyntax or ThisExpressionSyntax)
                    {
                        memberName = memberAccess.Name.Identifier.Text;
                        return true;
                    }

                    current = memberAccess.Expression;
                    continue;

                case MemberAccessExpressionSyntax memberAccess:
                    if (memberAccess.Expression is IdentifierNameSyntax or ThisExpressionSyntax)
                    {
                        memberName = memberAccess.Name.Identifier.Text;
                        return true;
                    }

                    current = memberAccess.Expression;
                    continue;

                case ParenthesizedExpressionSyntax parenthesized:
                    current = parenthesized.Expression;
                    continue;

                case QueryExpressionSyntax queryExpression:
                    current = queryExpression.FromClause.Expression;
                    continue;

                case CastExpressionSyntax cast:
                    current = cast.Expression;
                    continue;

                default:
                    return false;
            }
        }
    }

    private static bool IsInsideLambda(SyntaxNode node)
    {
        return node.Ancestors().Any(a =>
            a is SimpleLambdaExpressionSyntax
                or ParenthesizedLambdaExpressionSyntax
                or AnonymousMethodExpressionSyntax);
    }

    private static InvocationExpressionSyntax GetOutermostInvocationChain(InvocationExpressionSyntax invocation)
    {
        var current = invocation;
        while (current.Parent is MemberAccessExpressionSyntax or InvocationExpressionSyntax)
        {
            if (current.Parent is InvocationExpressionSyntax parentInvocation)
            {
                current = parentInvocation;
                continue;
            }

            if (current.Parent is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Parent is InvocationExpressionSyntax parentCall)
            {
                current = parentCall;
                continue;
            }

            break;
        }

        return current;
    }

    private static bool IsLikelyQueryChain(InvocationExpressionSyntax invocation)
    {
        var methodNames = GetInvocationChainMethodNames(invocation).ToArray();

        if (methodNames.Length == 0)
        {
            return false;
        }

        return methodNames.Any(name => TerminalMethods.Contains(name) || QueryChainMethods.Contains(name));
    }

    private static IEnumerable<string> GetInvocationChainMethodNames(InvocationExpressionSyntax invocation)
    {
        SyntaxNode? current = invocation;

        while (current is InvocationExpressionSyntax currentInvocation)
        {
            if (currentInvocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                yield break;
            }

            yield return memberAccess.Name.Identifier.Text;

            current = memberAccess.Expression;
        }
    }

    private static bool LooksLikeDbContextRoot(string? rootName)
    {
        if (string.IsNullOrWhiteSpace(rootName))
        {
            return false;
        }

        if (KnownDbContextRootNames.Contains(rootName))
        {
            return true;
        }

        return rootName.Contains("context", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeStaticTypeRoot(string? rootName)
    {
        if (string.IsNullOrWhiteSpace(rootName))
        {
            return false;
        }

        return char.IsUpper(rootName[0]);
    }

    private static bool TryExtractFromExpressionParameterHelperCall(
        SyntaxNode root,
        InvocationExpressionSyntax invocation,
        int cursorPosition,
        out string expression,
        out string? contextVariableName,
        ProjectSourceIndex? sourceIndex = null)
    {
        expression = string.Empty;
        contextVariableName = null;

        var helperName = GetInvokedMethodName(invocation);
        if (string.IsNullOrWhiteSpace(helperName))
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 0)
        {
            return false;
        }

        // Search the current file first, then fall back to sibling project files.
        // The sibling search covers the common pattern where the helper method is defined
        // in a service class in a different file from the call site (e.g. Program.cs calls
        // CustomerReadService.GetCustomerByIdAsync which lives in CustomerReadService.cs).
        IEnumerable<MethodDeclarationSyntax> allDeclarations = root
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>();

        if (sourceIndex is not null)
        {
            // Text-pre-filtered: only files that mention the method name are parsed.
            var methodRoots = sourceIndex.FindRootsForMethod(helperName);
            allDeclarations = allDeclarations.Concat(
                methodRoots.SelectMany(r => r.DescendantNodes().OfType<MethodDeclarationSyntax>()));
        }

        var candidates = allDeclarations
            .Where(m => string.Equals(m.Identifier.Text, helperName, StringComparison.Ordinal)
                && m.ParameterList.Parameters.Count == arguments.Count)
            .ToArray();

        foreach (var method in candidates)
        {
            var expressionParameterIndexes = GetExpressionParameterIndexes(method.ParameterList.Parameters);
            if (expressionParameterIndexes.Count == 0)
            {
                continue;
            }

            // Allow synthesis when the cursor is anywhere within the invocation -
            // including on the receiver, method name, or a non-expression argument.
            // The downstream guards (TryFindPrimaryQueryInvocation, IsLikelyQueryChain)
            // already reject methods whose bodies contain no EF query chain, so no
            // additional position narrowing is needed here.
            if (!invocation.Span.Contains(cursorPosition))
            {
                continue;
            }

            var queryInvocation = TryFindPrimaryQueryInvocation(method);
            if (queryInvocation is null)
            {
                continue;
            }

            ExpressionSyntax queryExpression = queryInvocation;
            queryExpression = TryInlineLocalQueryRoot(queryExpression, queryInvocation);
            queryExpression = StripTransparentQueryableCasts(queryExpression);

            var queryText = queryExpression.ToString();
            if (string.IsNullOrWhiteSpace(queryText))
            {
                continue;
            }

            queryText = SubstituteMethodParametersWithCallArguments(
                queryText,
                method.ParameterList.Parameters,
                arguments);

            ExpressionSyntax parsed;
            try
            {
                parsed = SyntaxFactory.ParseExpression(queryText);
            }
            catch
            {
                continue;
            }

            if (parsed is InvocationExpressionSyntax parsedInvocation
                && !IsLikelyQueryChain(parsedInvocation))
            {
                continue;
            }

            var rootContext = TryExtractRootContextVariable(parsed)
                ?? parsed.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Select(i => i.Identifier.Text)
                    .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(rootContext))
            {
                continue;
            }

            expression = parsed.ToString();
            contextVariableName = rootContext;
            return true;
        }

        return false;
    }

    private static string? GetInvokedMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            _ => null,
        };
    }

    private static List<int> GetExpressionParameterIndexes(SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        var indexes = new List<int>();
        for (var i = 0; i < parameters.Count; i++)
        {
            var typeText = parameters[i].Type?.ToString();
            if (string.IsNullOrWhiteSpace(typeText))
            {
                continue;
            }

            if (typeText.Contains("Expression<", StringComparison.Ordinal))
            {
                indexes.Add(i);
            }
        }

        return indexes;
    }

    private static InvocationExpressionSyntax? TryFindPrimaryQueryInvocation(MethodDeclarationSyntax method)
    {
        var body = method.Body;
        if (body is null)
        {
            return null;
        }

        InvocationExpressionSyntax? best = null;
        var bestSpan = -1;

        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var outermost = GetOutermostInvocationChain(invocation);
            if (!IsLikelyQueryChain(outermost))
            {
                continue;
            }

            var span = outermost.Span.Length;
            if (span > bestSpan)
            {
                bestSpan = span;
                best = outermost;
            }
        }

        return best;
    }

    private static string SubstituteMethodParametersWithCallArguments(
        string queryText,
        SeparatedSyntaxList<ParameterSyntax> parameters,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var result = queryText;

        for (var i = 0; i < parameters.Count && i < arguments.Count; i++)
        {
            var parameterName = parameters[i].Identifier.Text;
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                continue;
            }

            var argumentText = arguments[i].Expression.ToString();
            if (string.IsNullOrWhiteSpace(argumentText))
            {
                continue;
            }

            result = Regex.Replace(
                result,
                $@"\b{Regex.Escape(parameterName)}\b",
                argumentText);
        }

        return result;
    }
}
