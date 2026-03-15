using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

public static partial class LspSyntaxHelper
{
    private static ExpressionSyntax StripTransparentQueryableCasts(ExpressionSyntax expression)
    {
        var rewritten = new TransparentQueryableCastStripper().Visit(expression) as ExpressionSyntax;
        return rewritten ?? expression;
    }

    private static bool IsTransparentQueryCastType(TypeSyntax type)
    {
        var typeText = type.ToString();
        return typeText.Contains("IQueryable<", StringComparison.Ordinal)
               || typeText.Contains("IOrderedQueryable<", StringComparison.Ordinal)
               || typeText.Contains("IEnumerable<", StringComparison.Ordinal)
               || typeText.Contains("IOrderedEnumerable<", StringComparison.Ordinal)
               || typeText.Contains("IAsyncEnumerable<", StringComparison.Ordinal);
    }

    private static ExpressionSyntax TryInlineLocalQueryRoot(
        ExpressionSyntax expression,
        InvocationExpressionSyntax invocationContext)
    {
        var anchorStatement = invocationContext.FirstAncestorOrSelf<StatementSyntax>();
        if (anchorStatement is null)
        {
            return expression;
        }

        return InlineLeftMostIdentifierChain(expression, anchorStatement);
    }

    private static ExpressionSyntax InlineLeftMostIdentifierChain(
        ExpressionSyntax expression,
        StatementSyntax anchorStatement)
    {
        var currentExpression = expression;
        var currentAnchorStatement = anchorStatement;

        for (var depth = 0; depth < 32; depth++)
        {
            if (!TryGetLeftMostExpression(currentExpression, out var leftMostExpression)
                || leftMostExpression is not IdentifierNameSyntax identifier)
            {
                break;
            }

            if (!TryResolveLocalExpressionCore(
                    identifier.Identifier.ValueText,
                    currentAnchorStatement,
                    out var resolvedExpression,
                    out var resolvedAtStatement)
                || resolvedAtStatement is null)
            {
                break;
            }

            currentExpression = currentExpression.ReplaceNode(leftMostExpression, resolvedExpression.WithoutTrivia());
            currentAnchorStatement = resolvedAtStatement;
        }

        return currentExpression;
    }

    private static bool TryGetLeftMostExpression(ExpressionSyntax expression, out ExpressionSyntax leftMost)
    {
        leftMost = expression;
        var current = expression;

        while (true)
        {
            switch (current)
            {
                case InvocationExpressionSyntax invocation
                    when invocation.Expression is MemberAccessExpressionSyntax memberAccess:
                    current = memberAccess.Expression;
                    continue;

                case MemberAccessExpressionSyntax member:
                    current = member.Expression;
                    continue;

                case ParenthesizedExpressionSyntax parenthesized:
                    current = parenthesized.Expression;
                    continue;

                case CastExpressionSyntax cast:
                    current = cast.Expression;
                    continue;

                default:
                    leftMost = current;
                    return true;
            }
        }
    }

    private sealed class TransparentQueryableCastStripper : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitCastExpression(CastExpressionSyntax node)
        {
            var visited = (CastExpressionSyntax)base.VisitCastExpression(node)!;
            if (!IsTransparentQueryCastType(visited.Type))
                return visited;

            return visited.Expression.WithTriviaFrom(node);
        }
    }
}
