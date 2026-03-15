namespace EFQueryLens.Core.Scripting;

public sealed partial class QueryEvaluator
{
    // Stub generation and type inference helpers extracted from QueryEvaluator.cs
    // to keep EvaluateAsync flow readable.

    private static string BuildStubDeclaration(
        string name, string? rootId, TranslationRequest request, Type dbContextType)
    {
        if (!string.IsNullOrWhiteSpace(rootId)
            && string.Equals(name, rootId, StringComparison.Ordinal)
            && !string.Equals(name, request.ContextVariableName, StringComparison.Ordinal))
            return $"var {name} = {request.ContextVariableName};";

        // Gridify placeholders must win over generic member-access synthesis.
        // `query` is commonly used both as IGridifyQuery and as `query.Page` / `query.PageSize`.
        // If we synthesize it as anonymous object first, extension calls fail with CS1503.
        if (TryBuildGridifyStubDeclaration(name, request.Expression, dbContextType, out var gridifyStub))
            return gridifyStub;

        var memberTypes = InferMemberAccessTypes(name, request.Expression, dbContextType, request.UsingAliases);
        if (memberTypes.Count > 0)
        {
            var memberInitializers = string.Join(
                ", ",
                memberTypes.Select(kvp =>
                    $"{kvp.Key} = {BuildScalarPlaceholderExpression(kvp.Value)}"));

            return $"var {name} = new {{ {memberInitializers} }};";
        }

        var inferred = InferVariableType(name, request.Expression, dbContextType);
        inferred ??= InferMethodArgumentType(name, request.Expression, dbContextType);
        inferred ??= InferComparisonOperandType(name, request.Expression, dbContextType);
        if (inferred is not null)
        {
            var tn = ToCSharpTypeName(inferred);
            var value = BuildScalarPlaceholderExpression(inferred);
            return $"{tn} {name} = {value};";
        }

        if (LooksLikeBooleanConditionIdentifier(name, request.Expression))
            return $"bool {name} = true;";

        if (LooksLikeNumericArithmeticIdentifier(name, request.Expression))
            return $"int {name} = 1;";

        var elem = InferContainsElementType(name, request.Expression, dbContextType);
        if (elem is not null)
        {
            var en = ToCSharpTypeName(elem);
            var containsValues = BuildContainsPlaceholderValues(elem);
            return $"System.Collections.Generic.List<{en}> {name} = new() {{ {containsValues} }};";
        }

        var sel = InferSelectEntityType(name, request.Expression, dbContextType);
        if (sel is not null)
        {
            var sn = ToCSharpTypeName(sel);
            return $"System.Linq.Expressions.Expression<System.Func<{sn}, object>> {name} = _ => default!;";
        }

        var whereEntity = InferWhereEntityType(name, request.Expression, dbContextType);
        if (whereEntity is not null)
        {
            var wn = ToCSharpTypeName(whereEntity);
            return $"System.Linq.Expressions.Expression<System.Func<{wn}, bool>> {name} = _ => true;";
        }

        if (LooksLikeCancellationTokenArgument(name, request.Expression))
            return $"System.Threading.CancellationToken {name} = default;";

        return $"object {name} = default;";
    }

}
