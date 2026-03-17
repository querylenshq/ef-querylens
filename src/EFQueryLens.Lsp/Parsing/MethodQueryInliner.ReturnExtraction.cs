using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

public static partial class MethodQueryInliner
{
    private static ExpressionSyntax? TryExtractReturnedQueryExpression(MethodDeclarationSyntax method)
    {
        if (method.ExpressionBody is { Expression: { } expressionBody })
        {
            return StripTrailingTerminalMethods(UnwrapAwait(expressionBody));
        }

        if (method.Body == null)
        {
            return null;
        }

        for (var i = 0; i < method.Body.Statements.Count; i++)
        {
            if (method.Body.Statements[i] is not ReturnStatementSyntax returnStatement)
            {
                continue;
            }

            if (returnStatement.Expression is not { } returnExpr)
            {
                continue;
            }

            if (TryExtractQueryLikeReturnExpression(returnExpr, out var queryLikeReturn))
            {
                return InlineMethodLocalQueryRoot(method, queryLikeReturn, i);
            }
        }

        if (TryExtractQueryFromWrapperReturn(method, out var extractedFromWrapper, out var wrapperStatementIndex)
            && extractedFromWrapper is not null)
        {
            return InlineMethodLocalQueryRoot(method, extractedFromWrapper, wrapperStatementIndex);
        }

        return null;
    }

    private static bool TryExtractQueryLikeReturnExpression(
        ExpressionSyntax returnExpression,
        out ExpressionSyntax queryExpression)
    {
        queryExpression = null!;

        var unwrapped = UnwrapAwait(returnExpression);
        switch (unwrapped)
        {
            case InvocationExpressionSyntax:
            case MemberAccessExpressionSyntax:
            case IdentifierNameSyntax:
            case ThisExpressionSyntax:
            case ParenthesizedExpressionSyntax:
            case CastExpressionSyntax:
                queryExpression = StripTrailingTerminalMethods(unwrapped);
                return true;

            default:
                return false;
        }
    }

    private static bool TryExtractQueryFromWrapperReturn(
        MethodDeclarationSyntax method,
        out ExpressionSyntax? queryExpression,
        out int statementIndex)
    {
        queryExpression = null;
        statementIndex = -1;

        var statements = method.Body!.Statements;
        if (statements.Count == 0)
        {
            return false;
        }

        for (var i = statements.Count - 1; i >= 0; i--)
        {
            if (statements[i] is not ReturnStatementSyntax returnStatement
                || returnStatement.Expression is null)
            {
                continue;
            }

            var referencedNames = returnStatement.Expression
                .DescendantNodesAndSelf()
                .OfType<IdentifierNameSyntax>()
                .Select(id => id.Identifier.ValueText)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.Ordinal);

            if (referencedNames.Count == 0)
            {
                continue;
            }

            var bestScore = int.MinValue;
            ExpressionSyntax? bestExpression = null;
            var bestStatementIndex = -1;

            for (var j = i - 1; j >= 0; j--)
            {
                if (!TryExtractAssignedQueryExpression(statements[j], out var variableName, out var candidateExpression))
                {
                    continue;
                }

                if (!referencedNames.Contains(variableName))
                {
                    continue;
                }

                var score = ScoreAssignedQueryCandidate(candidateExpression);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestExpression = candidateExpression;
                    bestStatementIndex = j;
                }
            }

            if (bestExpression is not null)
            {
                queryExpression = bestExpression;
                statementIndex = bestStatementIndex;
                return true;
            }
        }

        return false;
    }

    private static ExpressionSyntax InlineMethodLocalQueryRoot(
        MethodDeclarationSyntax method,
        ExpressionSyntax expression,
        int anchorStatementIndex)
    {
        if (method.Body is null)
        {
            return StripTrailingTerminalMethods(UnwrapAwait(expression));
        }

        var statements = method.Body.Statements;
        if (statements.Count == 0)
        {
            return StripTrailingTerminalMethods(UnwrapAwait(expression));
        }

        var parameterNames = method.ParameterList.Parameters
            .Select(p => p.Identifier.ValueText)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.Ordinal);

        var current = StripTrailingTerminalMethods(UnwrapAwait(expression));
        var currentAnchor = anchorStatementIndex;

        for (var depth = 0; depth < 16; depth++)
        {
            if (!TryGetLeftMostExpression(current, out var leftMost)
                || leftMost is not IdentifierNameSyntax identifier)
            {
                break;
            }

            var name = identifier.Identifier.ValueText;
            if (parameterNames.Contains(name))
            {
                break;
            }

            if (!TryResolveLocalDeclarationExpression(statements, currentAnchor, name, out var replacement, out var replacementIndex))
            {
                break;
            }

            current = current.ReplaceNode(leftMost, replacement.WithoutTrivia());
            currentAnchor = replacementIndex;
        }

        return current;
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

                case MemberAccessExpressionSyntax memberAccess:
                    current = memberAccess.Expression;
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

    private static bool TryResolveLocalDeclarationExpression(
        SyntaxList<StatementSyntax> statements,
        int anchorStatementIndex,
        string identifier,
        out ExpressionSyntax expression,
        out int statementIndex)
    {
        expression = null!;
        statementIndex = -1;

        var start = Math.Min(anchorStatementIndex - 1, statements.Count - 1);
        for (var i = start; i >= 0; i--)
        {
            if (statements[i] is not LocalDeclarationStatementSyntax declaration)
            {
                continue;
            }

            foreach (var variable in declaration.Declaration.Variables)
            {
                if (!string.Equals(variable.Identifier.ValueText, identifier, StringComparison.Ordinal)
                    || variable.Initializer?.Value is not { } initializer)
                {
                    continue;
                }

                expression = StripTrailingTerminalMethods(UnwrapAwait(initializer));
                statementIndex = i;
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractAssignedQueryExpression(
        StatementSyntax statement,
        out string variableName,
        out ExpressionSyntax queryExpression)
    {
        variableName = string.Empty;
        queryExpression = null!;

        switch (statement)
        {
            case LocalDeclarationStatementSyntax localDeclaration:
            {
                foreach (var declarator in localDeclaration.Declaration.Variables)
                {
                    if (declarator.Initializer?.Value is not { } initializer)
                    {
                        continue;
                    }

                    if (!TryGetCandidateQueryExpression(initializer, out var candidate))
                    {
                        continue;
                    }

                    variableName = declarator.Identifier.ValueText;
                    queryExpression = candidate;
                    return true;
                }

                return false;
            }

            case ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax assignment
            }:
            {
                if (!TryGetAssignedIdentifierName(assignment.Left, out var assignedName))
                {
                    return false;
                }

                if (!TryGetCandidateQueryExpression(assignment.Right, out var assignedExpression))
                {
                    return false;
                }

                variableName = assignedName;
                queryExpression = assignedExpression;
                return true;
            }

            default:
                return false;
        }
    }

    private static bool TryGetCandidateQueryExpression(ExpressionSyntax expression, out ExpressionSyntax candidate)
    {
        candidate = null!;

        var unwrapped = UnwrapAwait(expression);
        if (unwrapped is not InvocationExpressionSyntax invocation
            || invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        var terminalName = memberAccess.Name.Identifier.ValueText;
        if (!TerminalMethods.Contains(terminalName))
        {
            return false;
        }

        candidate = invocation;
        return true;
    }

    private static bool TryGetAssignedIdentifierName(ExpressionSyntax left, out string identifierName)
    {
        identifierName = string.Empty;

        if (left is IdentifierNameSyntax identifier)
        {
            identifierName = identifier.Identifier.ValueText;
            return true;
        }

        return false;
    }

    private static int ScoreAssignedQueryCandidate(ExpressionSyntax expression)
    {
        var unwrapped = UnwrapAwait(expression);
        if (unwrapped is not InvocationExpressionSyntax invocation
            || invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return 0;
        }

        var terminalName = memberAccess.Name.Identifier.ValueText;
        var terminalScore = terminalName switch
        {
            "ToList" or "ToListAsync" or "ToArray" or "ToArrayAsync" or "ToDictionary" or "ToDictionaryAsync" => 100,
            "First" or "FirstOrDefault" or "FirstAsync" or "FirstOrDefaultAsync" => 60,
            "Single" or "SingleOrDefault" or "SingleAsync" or "SingleOrDefaultAsync" => 55,
            "Any" or "AnyAsync" => 40,
            "Count" or "CountAsync" or "LongCount" or "LongCountAsync" => 20,
            _ => 10,
        };

        var chainDepth = unwrapped.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Count();
        return terminalScore + chainDepth;
    }

    private static ExpressionSyntax UnwrapAwait(ExpressionSyntax expression)
    {
        if (expression is AwaitExpressionSyntax awaited)
        {
            return awaited.Expression;
        }

        return expression;
    }

    private static ExpressionSyntax StripTrailingTerminalMethods(ExpressionSyntax expression)
    {
        var current = expression;

        while (current is InvocationExpressionSyntax invocation &&
               invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
               TerminalMethods.Contains(memberAccess.Name.Identifier.ValueText))
        {
            current = memberAccess.Expression;
        }

        return current;
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

                case IdentifierNameSyntax identifier:
                    return identifier.Identifier.Text;

                case ThisExpressionSyntax:
                    return lastMemberName;

                default:
                    return lastMemberName;
            }
        }
    }
}
