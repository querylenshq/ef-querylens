using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

public static partial class LspSyntaxHelper
{
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
}
