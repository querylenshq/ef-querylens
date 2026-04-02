using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

public static partial class LspSyntaxHelper
{
    private static bool TryCollectQueryVariableFlow(
        string identifier,
        StatementSyntax anchorStatement,
        out ExpressionSyntax expression,
        out StatementSyntax? baseStatement)
    {
        expression = null!;
        baseStatement = null;

        if (!TryGetContainingStatements(anchorStatement, out var statements, out var anchorIndex))
        {
            return false;
        }

        ExpressionSyntax? baseExpression = null;
        StatementSyntax? currentBaseStatement = null;
        var appendOperations = new List<ExpressionSyntax>();

        for (var i = 0; i < anchorIndex; i++)
        {
            var statement = statements[i];

            if (TryGetDeclaredExpression(statement, identifier, out var declaredExpression))
            {
                if (baseExpression is null)
                {
                    baseExpression = declaredExpression;
                    currentBaseStatement = statement;
                    continue;
                }

                if (TryBuildAppendOperation(identifier, declaredExpression, out var appendExpression))
                {
                    appendOperations.Add(appendExpression);
                    continue;
                }

                baseExpression = declaredExpression;
                currentBaseStatement = statement;
                appendOperations.Clear();
                continue;
            }

            if (statement is IfStatementSyntax ifStatement
                && TryBuildConditionalAppendOperation(ifStatement, identifier, out var conditionalAppendExpression))
            {
                appendOperations.Add(conditionalAppendExpression);
            }
        }

        if (baseExpression is null || currentBaseStatement is null || appendOperations.Count == 0)
        {
            return false;
        }

        var current = baseExpression;
        foreach (var appendOperation in appendOperations)
        {
            current = ApplyAppendOperation(appendOperation, identifier, current);
        }

        expression = current;
        baseStatement = currentBaseStatement;
        return true;
    }

    private static bool TryGetContainingStatements(
        StatementSyntax anchorStatement,
        out List<StatementSyntax> statements,
        out int anchorIndex)
    {
        statements = null!;
        anchorIndex = -1;

        for (SyntaxNode? scope = anchorStatement.Parent; scope is not null; scope = scope.Parent)
        {
            if (!TryGetStatementContainer(scope, out var candidateStatements))
            {
                continue;
            }

            var index = candidateStatements.FindIndex(s => ReferenceEquals(s, anchorStatement));
            if (index < 0)
            {
                continue;
            }

            statements = candidateStatements;
            anchorIndex = index;
            return true;
        }

        return false;
    }

    private static bool TryBuildAppendOperation(
        string identifier,
        ExpressionSyntax value,
        out ExpressionSyntax appendExpression)
    {
        appendExpression = null!;

        if (IsSelfAppendExpression(identifier, value) || IsHelperPassthroughExpression(identifier, value))
        {
            appendExpression = value;
            return true;
        }

        if (value is ConditionalExpressionSyntax conditional
            && TryBuildConditionalAppendFromExpression(conditional, identifier, out var conditionalAppendExpression))
        {
            appendExpression = conditionalAppendExpression;
            return true;
        }

        return false;
    }

    private static bool TryBuildConditionalAppendOperation(
        IfStatementSyntax ifStatement,
        string identifier,
        out ExpressionSyntax appendExpression)
    {
        appendExpression = null!;

        var elseClause = ifStatement.Else;
        if (elseClause is null)
        {
            // Policy: when the condition cannot be evaluated and there is no else branch,
            // keep the baseline query (ignore the if mutation).
            return false;
        }

        if (!TryExtractSingleAssignmentExpression(elseClause.Statement, identifier, out var elseExpression)
            || !TryBuildAppendOperation(identifier, elseExpression, out var elseAppend))
        {
            return false;
        }

        // Policy: when the condition cannot be evaluated, choose the explicit else path.
        appendExpression = elseAppend;
        return true;
    }

    private static bool TryBuildConditionalAppendFromExpression(
        ConditionalExpressionSyntax conditional,
        string identifier,
        out ExpressionSyntax appendExpression)
    {
        appendExpression = null!;

        if (!TryBuildAppendOperation(identifier, conditional.WhenFalse, out var whenFalseAppend))
        {
            return false;
        }

        // Policy: for `cond ? then : else`, use the else branch when condition is unknown.
        appendExpression = whenFalseAppend;
        return true;
    }

    private static bool TryExtractSingleAssignmentExpression(
        StatementSyntax statement,
        string identifier,
        out ExpressionSyntax expression)
    {
        expression = null!;

        if (TryGetDeclaredExpression(statement, identifier, out expression))
        {
            return true;
        }

        if (statement is not BlockSyntax block)
        {
            return false;
        }

        ExpressionSyntax? found = null;
        foreach (var innerStatement in block.Statements)
        {
            if (!TryGetDeclaredExpression(innerStatement, identifier, out var candidate))
            {
                continue;
            }

            if (found is not null)
            {
                expression = null!;
                return false;
            }

            found = candidate;
        }

        if (found is null)
        {
            return false;
        }

        expression = found;
        return true;
    }

    private static bool IsSelfAppendExpression(string identifier, ExpressionSyntax value)
    {
        if (!TryGetLeftMostExpression(value, out var leftMost)
            || leftMost is not IdentifierNameSyntax rootIdentifier)
        {
            return false;
        }

        return string.Equals(rootIdentifier.Identifier.ValueText, identifier, StringComparison.Ordinal);
    }

    private static bool IsHelperPassthroughExpression(string identifier, ExpressionSyntax value)
    {
        if (value is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        if (invocation.Expression is not IdentifierNameSyntax)
        {
            return false;
        }

        return invocation.ArgumentList.Arguments.Any(a =>
            a.Expression is IdentifierNameSyntax argIdentifier
            && string.Equals(argIdentifier.Identifier.ValueText, identifier, StringComparison.Ordinal));
    }

    private static ExpressionSyntax ApplyAppendOperation(
        ExpressionSyntax appendExpression,
        string identifier,
        ExpressionSyntax current)
    {
        var rewritten = new TrackedIdentifierRewriter(identifier, current.WithoutTrivia()).Visit(appendExpression);
        return rewritten as ExpressionSyntax ?? appendExpression;
    }

    private sealed class TrackedIdentifierRewriter : CSharpSyntaxRewriter
    {
        private readonly string _identifier;
        private readonly ExpressionSyntax _replacement;

        public TrackedIdentifierRewriter(string identifier, ExpressionSyntax replacement)
        {
            _identifier = identifier;
            _replacement = replacement;
        }

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            if (string.Equals(node.Parameter.Identifier.ValueText, _identifier, StringComparison.Ordinal))
            {
                return node;
            }

            return base.VisitSimpleLambdaExpression(node);
        }

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            if (node.ParameterList.Parameters.Any(p =>
                    string.Equals(p.Identifier.ValueText, _identifier, StringComparison.Ordinal)))
            {
                return node;
            }

            return base.VisitParenthesizedLambdaExpression(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (string.Equals(node.Identifier.ValueText, _identifier, StringComparison.Ordinal))
            {
                return _replacement.WithTriviaFrom(node);
            }

            return base.VisitIdentifierName(node);
        }
    }
}