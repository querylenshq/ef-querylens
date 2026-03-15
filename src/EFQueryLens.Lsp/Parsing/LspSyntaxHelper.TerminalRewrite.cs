using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

public static partial class LspSyntaxHelper
{
    private static ExpressionSyntax? StripTerminalInvocation(InvocationExpressionSyntax invocation)
    {
        ExpressionSyntax targetExpression = invocation;

        while (targetExpression is InvocationExpressionSyntax terminalInvocation
               && terminalInvocation.Expression is MemberAccessExpressionSyntax terminalAccess
               && TerminalMethods.Contains(terminalAccess.Name.Identifier.Text))
        {
            if (TryRewriteTerminalInvocation(
                    terminalAccess.Expression,
                    terminalAccess.Name.Identifier.Text,
                    terminalInvocation.ArgumentList.Arguments,
                    terminalInvocation,
                    out var rewritten))
            {
                targetExpression = rewritten;
                continue;
            }

            targetExpression = terminalAccess.Expression;
        }

        return targetExpression;
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

                case CastExpressionSyntax cast:
                    current = cast.Expression;
                    continue;

                default:
                    return false;
            }
        }
    }

    private static bool TryRewriteTerminalInvocation(
        ExpressionSyntax source,
        string terminalMethodName,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        InvocationExpressionSyntax invocationContext,
        out ExpressionSyntax rewritten)
    {
        var query = source;

        if (query is IdentifierNameSyntax queryIdentifier
            && TryResolveLocalExpression(queryIdentifier.Identifier.ValueText, invocationContext, out var resolvedQuery))
        {
            query = resolvedQuery;
        }

        if (TryExtractPredicateArgument(terminalMethodName, arguments, invocationContext, out var predicateArgument))
        {
            query = CreateWhereCall(query, predicateArgument);
        }

        if (CountTerminalMethods.Contains(terminalMethodName))
        {
            var isLongCount = terminalMethodName.StartsWith("LongCount", StringComparison.OrdinalIgnoreCase);
            rewritten = CreateCountProjectionCall(query, isLongCount);
            return true;
        }

        if (TakeOneTerminalMethods.Contains(terminalMethodName))
        {
            rewritten = CreateTakeCall(query, 1);
            return true;
        }

        if (TakeTwoTerminalMethods.Contains(terminalMethodName))
        {
            rewritten = CreateTakeCall(query, 2);
            return true;
        }

        if (PredicateTerminalMethods.Contains(terminalMethodName))
        {
            rewritten = query;
            return true;
        }

        rewritten = source;
        return false;
    }

    private static bool TryExtractPredicateArgument(
        string terminalMethodName,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        InvocationExpressionSyntax invocationContext,
        out ExpressionSyntax predicateArgument)
    {
        predicateArgument = null!;

        if (!PredicateTerminalMethods.Contains(terminalMethodName) || arguments.Count == 0)
            return false;

        foreach (var argument in arguments)
        {
            if (IsCancellationTokenArgument(argument))
                continue;

            if (argument.Expression is LambdaExpressionSyntax
                || argument.Expression is AnonymousMethodExpressionSyntax
                || argument.Expression is MemberAccessExpressionSyntax)
            {
                predicateArgument = argument.Expression;
                return true;
            }

            if (argument.Expression is IdentifierNameSyntax identifier)
            {
                if (TryResolveLocalPredicateExpression(identifier.Identifier.ValueText, invocationContext, out var resolvedPredicate))
                {
                    predicateArgument = resolvedPredicate;
                    return true;
                }

                predicateArgument = identifier;
                return true;
            }
        }

        return false;
    }

    private static bool IsCancellationTokenArgument(ArgumentSyntax argument)
    {
        if (argument.NameColon?.Name.Identifier.ValueText is { } named
            && string.Equals(named, "cancellationToken", StringComparison.OrdinalIgnoreCase))
            return true;

        if (argument.Expression is IdentifierNameSyntax id)
        {
            var n = id.Identifier.ValueText;
            return string.Equals(n, "ct", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(n, "cancellationToken", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static InvocationExpressionSyntax CreateWhereCall(
        ExpressionSyntax source,
        ExpressionSyntax predicate)
    {
        return SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                source,
                SyntaxFactory.IdentifierName("Where")),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(predicate))));
    }

    private static InvocationExpressionSyntax CreateTakeCall(ExpressionSyntax source, int count)
    {
        return SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                source,
                SyntaxFactory.IdentifierName("Take")),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(
                        SyntaxFactory.LiteralExpression(
                            SyntaxKind.NumericLiteralExpression,
                            SyntaxFactory.Literal(count))))));
    }

    private static ExpressionSyntax CreateCountProjectionCall(ExpressionSyntax source, bool useLongCount)
    {
        var countMethod = useLongCount ? "LongCount" : "Count";
        return SyntaxFactory.ParseExpression($"({source}).GroupBy(_ => 1).Select(g => g.{countMethod}())");
    }
}
