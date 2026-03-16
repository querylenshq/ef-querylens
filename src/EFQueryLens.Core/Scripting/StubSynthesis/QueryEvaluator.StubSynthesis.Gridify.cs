using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Core.Scripting;

public sealed partial class QueryEvaluator
{
    private static bool TryBuildGridifyStubDeclaration(
        string variableName,
        string expression,
        Type dbContextType,
        out string declaration)
    {
        declaration = string.Empty;

        if (!TryGetGridifyArgumentRole(variableName, expression, out var role, out var sourceExpression))
            return false;

        if (role == GridifyArgumentRole.Query)
        {
            declaration = $"global::Gridify.IGridifyQuery {variableName} = new global::Gridify.GridifyQuery();";
            return true;
        }

        if (role != GridifyArgumentRole.Mapper || sourceExpression is null)
            return false;

        var entityType = InferQueryEntityTypeFromSource(sourceExpression, dbContextType);
        if (entityType is null)
            return false;

        declaration = $"global::Gridify.IGridifyMapper<{ToCSharpTypeName(entityType)}>? {variableName} = null;";
        return true;
    }

    private static bool TryGetGridifyArgumentRole(
        string variableName,
        string expression,
        out GridifyArgumentRole role,
        out ExpressionSyntax? sourceExpression)
    {
        role = GridifyArgumentRole.None;
        sourceExpression = null;

        if (!expression.Contains("ApplyFilteringAndOrdering", StringComparison.Ordinal))
            return false;

        var parsed = SyntaxFactory.ParseExpression(expression);
        foreach (var invocation in parsed.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            var methodName = GetInvocationMethodName(invocation);
            if (!string.Equals(methodName, "ApplyFilteringAndOrdering", StringComparison.Ordinal))
                continue;

            var isExtensionCall = invocation.Expression is MemberAccessExpressionSyntax;
            var args = invocation.ArgumentList.Arguments;

            var queryArgIndex = isExtensionCall ? 0 : 1;
            var mapperArgIndex = isExtensionCall ? 1 : 2;

            if (args.Count <= queryArgIndex)
                continue;

            if (TryGetSimpleIdentifier(args[queryArgIndex].Expression, out var queryArg)
                && string.Equals(queryArg, variableName, StringComparison.Ordinal))
            {
                role = GridifyArgumentRole.Query;
                sourceExpression = isExtensionCall
                    ? ((MemberAccessExpressionSyntax)invocation.Expression).Expression
                    : args[0].Expression;
                return true;
            }

            if (args.Count <= mapperArgIndex)
                continue;

            if (TryGetSimpleIdentifier(args[mapperArgIndex].Expression, out var mapperArg)
                && string.Equals(mapperArg, variableName, StringComparison.Ordinal))
            {
                role = GridifyArgumentRole.Mapper;
                sourceExpression = isExtensionCall
                    ? ((MemberAccessExpressionSyntax)invocation.Expression).Expression
                    : args[0].Expression;
                return true;
            }
        }

        return false;
    }

    private static string? GetInvocationMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            _ => null,
        };
    }

    private static bool TryGetSimpleIdentifier(ExpressionSyntax expression, out string identifier)
    {
        switch (expression)
        {
            case IdentifierNameSyntax id:
                identifier = id.Identifier.ValueText;
                return true;

            case PostfixUnaryExpressionSyntax postfix
                when postfix.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression:
                return TryGetSimpleIdentifier(postfix.Operand, out identifier);

            case ParenthesizedExpressionSyntax parenthesized:
                return TryGetSimpleIdentifier(parenthesized.Expression, out identifier);

            case CastExpressionSyntax cast:
                return TryGetSimpleIdentifier(cast.Expression, out identifier);

            default:
                identifier = string.Empty;
                return false;
        }
    }

    private static Type? InferQueryEntityTypeFromSource(ExpressionSyntax sourceExpression, Type dbContextType)
    {
        ExpressionSyntax current = sourceExpression;

        while (true)
        {
            switch (current)
            {
                case InvocationExpressionSyntax invocation
                    when invocation.Expression is MemberAccessExpressionSyntax memberAccess:
                    current = memberAccess.Expression;
                    continue;

                case MemberAccessExpressionSyntax member:
                    var prop = dbContextType.GetProperty(member.Name.Identifier.ValueText);
                    if (prop is not null)
                    {
                        var elementType = TryGetQueryableElementType(prop.PropertyType);
                        if (elementType is not null)
                            return elementType;
                    }

                    current = member.Expression;
                    continue;

                case ParenthesizedExpressionSyntax parenthesized:
                    current = parenthesized.Expression;
                    continue;

                case CastExpressionSyntax cast:
                    current = cast.Expression;
                    continue;

                default:
                    return null;
            }
        }
    }

    private static Type? TryGetQueryableElementType(Type queryableType)
    {
        if (queryableType.IsGenericType && queryableType.GetGenericTypeDefinition() == typeof(IQueryable<>))
            return queryableType.GetGenericArguments()[0];

        var iqueryable = queryableType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQueryable<>));
        if (iqueryable is not null)
            return iqueryable.GetGenericArguments()[0];

        if (queryableType.IsGenericType && queryableType.GetGenericArguments().Length == 1)
            return queryableType.GetGenericArguments()[0];

        return null;
    }

    private enum GridifyArgumentRole
    {
        None,
        Query,
        Mapper,
    }
}
