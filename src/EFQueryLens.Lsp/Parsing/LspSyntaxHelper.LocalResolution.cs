using System.Collections.Generic;
using System.Linq;
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

    private static bool TryResolveLocalPredicateExpression(
        string identifier,
        InvocationExpressionSyntax invocationContext,
        out ExpressionSyntax predicate)
    {
        predicate = null!;

        if (!TryResolveLocalExpression(identifier, invocationContext, out var expression))
            return false;

        return TryUnwrapPredicateExpression(expression, out predicate);
    }

    private static bool TryResolveLocalExpression(
        string identifier,
        InvocationExpressionSyntax invocationContext,
        out ExpressionSyntax expression)
    {
        expression = null!;

        if (!TryResolveLocalExpression(identifier, invocationContext, out expression, out var resolvedAtStatement)
            || resolvedAtStatement is null)
        {
            return false;
        }

        expression = InlineLeftMostIdentifierChain(expression, resolvedAtStatement);
        return true;
    }

    private static bool TryResolveLocalExpression(
        string identifier,
        InvocationExpressionSyntax invocationContext,
        out ExpressionSyntax expression,
        out StatementSyntax? resolvedAtStatement)
    {
        expression = null!;
        resolvedAtStatement = null;

        var anchorStatement = invocationContext.FirstAncestorOrSelf<StatementSyntax>();
        if (anchorStatement is null)
            return false;

        return TryResolveLocalExpressionCore(identifier, anchorStatement, out expression, out resolvedAtStatement);
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

    private static bool TryResolveLocalExpressionCore(
        string identifier,
        StatementSyntax anchorStatement,
        out ExpressionSyntax expression,
        out StatementSyntax? resolvedAtStatement)
    {
        expression = null!;
        resolvedAtStatement = null;

        for (SyntaxNode? scope = anchorStatement.Parent; scope is not null; scope = scope.Parent)
        {
            if (!TryGetStatementContainer(scope, out var statements))
                continue;

            var anchorIndex = statements.FindIndex(s => ReferenceEquals(s, anchorStatement));
            if (anchorIndex < 0)
                continue;

            for (var i = anchorIndex - 1; i >= 0; i--)
            {
                var statement = statements[i];
                if (TryGetDeclaredExpression(statement, identifier, out var declaredExpression))
                {
                    if (declaredExpression is IdentifierNameSyntax nestedIdentifier)
                    {
                        if (TryResolveLocalExpressionCore(
                                nestedIdentifier.Identifier.ValueText,
                                statement,
                                out expression,
                                out resolvedAtStatement))
                        {
                            return true;
                        }

                        continue;
                    }

                    expression = declaredExpression;
                    resolvedAtStatement = statement;
                    return true;
                }
            }

            var outerStatement = scope.Parent?.FirstAncestorOrSelf<StatementSyntax>();
            if (outerStatement is null || ReferenceEquals(outerStatement, anchorStatement))
                break;

            anchorStatement = outerStatement;
        }

        return false;
    }

    private static bool TryGetStatementContainer(SyntaxNode scope, out List<StatementSyntax> statements)
    {
        switch (scope)
        {
            case BlockSyntax block:
                statements = block.Statements.ToList();
                return true;

            case SwitchSectionSyntax section:
                statements = section.Statements.ToList();
                return true;

            case CompilationUnitSyntax compilationUnit:
                statements = compilationUnit.Members
                    .OfType<GlobalStatementSyntax>()
                    .Select(m => m.Statement)
                    .ToList();
                return statements.Count > 0;

            default:
                statements = null!;
                return false;
        }
    }

    private static bool TryGetDeclaredExpression(
        StatementSyntax statement,
        string identifier,
        out ExpressionSyntax expression)
    {
        expression = null!;

        if (statement is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (!string.Equals(variable.Identifier.ValueText, identifier, StringComparison.Ordinal))
                    continue;

                if (variable.Initializer?.Value is not null)
                {
                    expression = variable.Initializer.Value;
                    return true;
                }
            }
        }

        if (statement is ExpressionStatementSyntax expressionStatement
            && expressionStatement.Expression is AssignmentExpressionSyntax assignment
            && assignment.Left is IdentifierNameSyntax leftIdentifier
            && string.Equals(leftIdentifier.Identifier.ValueText, identifier, StringComparison.Ordinal))
        {
            expression = assignment.Right;
            return true;
        }

        return false;
    }

    private static bool TryUnwrapPredicateExpression(ExpressionSyntax expression, out ExpressionSyntax predicate)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return TryUnwrapPredicateExpression(parenthesized.Expression, out predicate);

            case CastExpressionSyntax cast:
                return TryUnwrapPredicateExpression(cast.Expression, out predicate);

            case LambdaExpressionSyntax:
            case AnonymousMethodExpressionSyntax:
                predicate = expression;
                return true;

            default:
                predicate = null!;
                return false;
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
