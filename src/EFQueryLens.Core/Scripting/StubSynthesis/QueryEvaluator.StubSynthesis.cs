using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using EFQueryLens.Core.Contracts;

namespace EFQueryLens.Core.Scripting.Evaluation;

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

        // Use LSP-provided authoritative type when available — skip heuristics entirely.
        // A null result means the supplied type is unusable as a local's type (e.g. a static
        // class like 'Math' mis-inferred from `var page = Math.Max(...)`). In that case we fall
        // through to the usage-based heuristics below, which recover the real type (e.g. int).
        if (request.LocalVariableTypes.TryGetValue(name, out var knownTypeName)
            && !string.IsNullOrWhiteSpace(knownTypeName))
        {
            // LSP may supply a non-nullable value type while the expression uses Nullable<T>.Value.
            if (LooksLikeNullableValueAccess(name, request.Expression)
                && !IsNullableTypeName(knownTypeName))
            {
                var nullableTypeName = $"{knownTypeName.TrimEnd('?')}?";
                if (BuildStubFromTypeName(nullableTypeName, name, dbContextType) is { } nullableStub)
                    return nullableStub;
            }

            if (BuildStubFromTypeName(knownTypeName, name, dbContextType) is { } lspStub)
                return lspStub;
        }

        // Gridify placeholders must win over generic member-access synthesis.
        // `query` is commonly used both as IGridifyQuery and as `query.Page` / `query.PageSize`.
        // If we synthesize it as anonymous object first, extension calls fail with CS1503.
        if (TryBuildGridifyStubDeclaration(name, request.Expression, dbContextType, out var gridifyStub))
            return gridifyStub;

        if (TryBuildSelectContainsCollectionStub(name, request.Expression, dbContextType) is { } selectContainsStub)
            return selectContainsStub;

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

        // Collection receivers like `cnDeviceIds.Contains(x.Id)` often appear after `&&` in
        // predicates; infer them before the boolean-condition heuristic mis-types them as bool.
        var elem = InferContainsElementType(name, request.Expression, dbContextType);
        if (elem is not null)
        {
            var en = ToCSharpTypeName(elem);
            var containsValues = BuildContainsPlaceholderValues(elem);
            return $"System.Collections.Generic.List<{en}> {name} = new() {{ {containsValues} }};";
        }

        if (LooksLikeBooleanConditionIdentifier(name, request.Expression))
            return $"bool {name} = true;";

        if (LooksLikeNumericArithmeticIdentifier(name, request.Expression))
            return $"int {name} = 1;";

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

        if (LooksLikeSelectReceiver(name, request.Expression))
            return $"System.Collections.Generic.List<object> {name} = new();";

        return $"System.Collections.Generic.List<object> {name} = new();";
    }

    /// <summary>
    /// Builds a local-variable stub declaration for a known type name. Returns <c>null</c> when
    /// the type name cannot legally be a variable's type — i.e. it resolves to a static, abstract
    /// or interface type — so callers can fall back to other inference instead of emitting code
    /// that fails to compile (CS0723 "variable of static type" / CS0716 "convert to static type").
    /// </summary>
    private static string NormalizeTypeNameForStub(string typeName)
        => typeName.Trim().Replace("global::", string.Empty, StringComparison.Ordinal);

    private static string? BuildStubFromTypeName(string typeName, string varName, Type dbContextType)
    {
        typeName = NormalizeTypeNameForStub(typeName);
        if (TryBuildNullableValueTypeStub(typeName, varName, out var nullableStub))
            return nullableStub;

        return typeName switch
        {
            "int" or "Int32" or "System.Int32" => $"int {varName} = 0;",
            "long" or "Int64" or "System.Int64" => $"long {varName} = 0L;",
            "short" or "Int16" or "System.Int16" => $"short {varName} = 0;",
            "byte" or "Byte" or "System.Byte" => $"byte {varName} = 0;",
            "uint" or "UInt32" or "System.UInt32" => $"uint {varName} = 0u;",
            "ulong" or "UInt64" or "System.UInt64" => $"ulong {varName} = 0ul;",
            "bool" or "Boolean" or "System.Boolean" => $"bool {varName} = false;",
            // Include nullable reference-type variants — the CLR makes no distinction
            // for reference types, and 'string ""' satisfies both 'string' and 'string?'.
            "string" or "String" or "System.String"
                or "string?" or "String?" or "System.String?" => $"string {varName} = \"\";",
            "char" or "Char" or "System.Char" => $"char {varName} = '\\0';",
            "decimal" or "Decimal" or "System.Decimal" => $"decimal {varName} = 0m;",
            "double" or "Double" or "System.Double" => $"double {varName} = 0.0;",
            "float" or "Single" or "System.Single" => $"float {varName} = 0.0f;",
            "Guid" or "System.Guid" => $"System.Guid {varName} = System.Guid.Empty;",
            "DateTime" or "System.DateTime" => $"System.DateTime {varName} = System.DateTime.UtcNow;",
            "DateTimeOffset" or "System.DateTimeOffset" => $"System.DateTimeOffset {varName} = System.DateTimeOffset.UtcNow;",
            "DateOnly" or "System.DateOnly" => $"System.DateOnly {varName} = System.DateOnly.FromDateTime(System.DateTime.Today);",
            "TimeOnly" or "System.TimeOnly" => $"System.TimeOnly {varName} = System.TimeOnly.MinValue;",
            "CancellationToken" or "System.Threading.CancellationToken" => $"System.Threading.CancellationToken {varName} = default;",
            var tn when tn.EndsWith("[]", StringComparison.Ordinal)
                => $"{tn[..^2]}[] {varName} = System.Array.Empty<{tn[..^2]}>();",
            var tn when TryExtractCollectionElementType(tn, out var elem)
                => BuildListStubDeclaration(varName, elem, dbContextType),
            // Expression<Func<...>> — generate a typed lambda rather than GetUninitializedObject.
            // An uninitialized Expression has null internal nodes (Body, Parameters, etc.);
            // EF Core walks the expression tree to produce SQL and will throw on any null node.
            // A proper lambda compiles to a valid expression tree that EF Core can translate:
            //   predicate (bool return)  → _ => true   (matches all rows — safe for WHERE)
            //   projection (other return) → _ => default! (typed null — best-effort for SELECT)
            var tn when IsExpressionFuncTypeName(tn)
                => IsBoolPredicateExpression(tn)
                    ? $"{tn} {varName} = _ => true;"
                    : $"{tn} {varName} = _ => default!;",
            // Unknown complex type (user-defined DTO, entity, etc.) — delegated so we can guard
            // against static/abstract/interface type names that cannot be a variable's type.
            var tn => BuildComplexTypeStub(tn, varName, dbContextType),
        };
    }

    /// <summary>
    /// Stub for an unknown but presumed-instantiable complex type (user DTO/entity). Uses
    /// <c>GetUninitializedObject</c> so the instance is non-null — EF Core must be able to
    /// evaluate captured parameter expressions (e.g. <c>model.PlanningCaseId</c>) at runtime, and
    /// a null reference throws before SQL is produced. Returns <c>null</c> when the type name
    /// resolves to a static / abstract / interface type, which can be neither declared as a
    /// variable (CS0723) nor used as a cast target (CS0716) — the caller then falls back to other
    /// inference. (Strips the nullable-reference '?' — <c>typeof()</c> has no CLR distinction.)
    /// </summary>
    private static string? BuildComplexTypeStub(string typeName, string varName, Type dbContextType)
    {
        var normalized = NormalizeTypeNameForStub(typeName);
        if (TryBuildNullableValueTypeStub(normalized, varName, out var nullableStub))
            return nullableStub;

        var bare = normalized.TrimEnd('?');

        if (IsStringTypeName(bare))
            return $"string {varName} = \"\";";

        if (IsNonInstantiableTypeName(bare, dbContextType))
            return null;

        var resolved = TryResolveTypeName(bare, dbContextType);
        if (resolved == typeof(string))
            return $"string {varName} = \"\";";

        if (resolved is { IsInterface: true } or { IsAbstract: true })
            return null;

        if (resolved is null && LooksLikeUnresolvedInterfaceName(bare))
            return null;

        return $"var {varName} = ({bare})global::System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof({bare}));";
    }

    private static bool IsStringTypeName(string typeName)
        => typeName is "string" or "String" or "System.String" or "string?" or "String?" or "System.String?";

    private static bool IsNullableTypeName(string typeName)
    {
        var normalized = NormalizeTypeNameForStub(typeName);
        return normalized.EndsWith('?')
               || normalized.StartsWith("Nullable<", StringComparison.Ordinal);
    }

    private static bool LooksLikeNullableValueAccess(string variableName, string expression)
        => !string.IsNullOrWhiteSpace(variableName)
           && !string.IsNullOrWhiteSpace(expression)
           && expression.Contains($"{variableName}.Value", StringComparison.Ordinal);

    private static bool TryBuildNullableValueTypeStub(string typeName, string varName, out string? stub)
    {
        stub = null;
        string inner;
        if (typeName.EndsWith('?'))
        {
            inner = typeName[..^1].Trim();
        }
        else if (typeName.StartsWith("Nullable<", StringComparison.Ordinal) && typeName.EndsWith('>'))
        {
            inner = typeName["Nullable<".Length..^1].Trim();
        }
        else
        {
            return false;
        }

        stub = inner switch
        {
            "Guid" or "System.Guid" => $"System.Guid? {varName} = System.Guid.Empty;",
            "int" or "Int32" or "System.Int32" => $"int? {varName} = 0;",
            "long" or "Int64" or "System.Int64" => $"long? {varName} = 0L;",
            "short" or "Int16" or "System.Int16" => $"short? {varName} = 0;",
            "byte" or "Byte" or "System.Byte" => $"byte? {varName} = 0;",
            "bool" or "Boolean" or "System.Boolean" => $"bool? {varName} = false;",
            "decimal" or "Decimal" or "System.Decimal" => $"decimal? {varName} = 0m;",
            "double" or "Double" or "System.Double" => $"double? {varName} = 0.0;",
            "float" or "Single" or "System.Single" => $"float? {varName} = 0.0f;",
            "DateTime" or "System.DateTime" => $"System.DateTime? {varName} = System.DateTime.UtcNow;",
            "DateTimeOffset" or "System.DateTimeOffset" => $"System.DateTimeOffset? {varName} = System.DateTimeOffset.UtcNow;",
            "DateOnly" or "System.DateOnly" => $"System.DateOnly? {varName} = System.DateOnly.FromDateTime(System.DateTime.Today);",
            "TimeOnly" or "System.TimeOnly" => $"System.TimeOnly? {varName} = System.TimeOnly.MinValue;",
            _ => null,
        };

        return stub is not null;
    }

    private static bool LooksLikeUnresolvedInterfaceName(string typeName)
    {
        var simple = typeName.Contains('.')
            ? typeName[(typeName.LastIndexOf('.') + 1)..]
            : typeName;

        return simple.Length > 2
               && simple[0] == 'I'
               && char.IsUpper(simple[1])
               && char.IsLower(simple[2]);
    }

    // Type names already proven (non-)instantiable, to avoid repeated reflection in the retry loop.
    private static readonly ConcurrentDictionary<string, bool> _nonInstantiableTypeNameCache =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Returns true when <paramref name="typeName"/> resolves to a static, abstract or interface
    /// type — none of which can be a local variable's type. Unresolvable names return false so
    /// existing behaviour is preserved for genuinely unknown (but possibly valid) type names.
    /// </summary>
    private static bool IsNonInstantiableTypeName(string typeName, Type dbContextType)
    {
        var bare = typeName.Trim();
        if (bare.Length == 0)
            return false;

        return _nonInstantiableTypeNameCache.GetOrAdd(bare, key =>
        {
            var resolved = TryResolveTypeName(key, dbContextType);
            // A static class is reported by reflection as abstract + sealed, so IsAbstract covers
            // both static and abstract classes; IsInterface covers interfaces.
            return resolved is not null && (resolved.IsAbstract || resolved.IsInterface);
        });
    }

    /// <summary>
    /// Best-effort resolution of a simple or namespace-qualified type name to a <see cref="Type"/>,
    /// searching the runtime, the <c>System</c> namespace (for BCL helpers like <c>Math</c>), and
    /// the DbContext's load context. Generic / array / collection / Expression forms never reach
    /// here — they are matched by earlier branches of <see cref="BuildStubFromTypeName"/>.
    /// </summary>
    private static Type? TryResolveTypeName(string typeName, Type dbContextType)
    {
        var direct = Type.GetType(typeName, throwOnError: false);
        if (direct is not null)
            return direct;

        if (!typeName.Contains('.'))
        {
            // Common static/helper types (Math, Console, Convert, …) live in System.
            var system = Type.GetType("System." + typeName, throwOnError: false);
            if (system is not null)
                return system;
        }

        var alc = AssemblyLoadContext.GetLoadContext(dbContextType.Assembly);
        var assemblies = (IEnumerable<Assembly>?)alc?.Assemblies ?? AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            var t = asm.GetType(typeName, throwOnError: false);
            if (t is not null)
                return t;
        }

        return null;
    }

    private static string BuildListStubDeclaration(string varName, string elementType, Type dbContextType)
    {
        var initializer = BuildCollectionElementInitializer(elementType, dbContextType);
        return $"System.Collections.Generic.List<{elementType}> {varName} = new() {{ {initializer} }};";
    }

    private static string BuildCollectionElementInitializer(string elementType, Type dbContextType)
    {
        var bare = NormalizeTypeNameForStub(elementType).TrimEnd('?');
        if (IsStringTypeName(bare))
            return BuildScalarPlaceholderExpression(typeof(string));

        var resolved = TryResolveTypeName(bare, dbContextType);

        if (resolved is not null)
        {
            if (resolved.IsValueType || resolved == typeof(string))
                return BuildScalarPlaceholderExpression(resolved);

            if (!resolved.IsInterface && !resolved.IsAbstract)
            {
                var tn = ToCSharpTypeName(resolved);
                return $"({tn})global::System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof({tn}))";
            }
        }

        if (LooksLikeUnresolvedInterfaceName(bare) || IsNonInstantiableTypeName(bare, dbContextType))
        {
            return "new object()";
        }

        return $"({bare})global::System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof({bare}))";
    }

    private static bool TryExtractCollectionElementType(string typeName, out string elementType)
    {
        elementType = string.Empty;
        var lt = typeName.IndexOf('<');
        var gt = typeName.LastIndexOf('>');
        if (lt < 0 || gt < 0 || gt <= lt) return false;

        var outer = typeName[..lt].Trim();
        if (outer is not ("List" or "IList" or "ICollection" or "IEnumerable"
            or "IReadOnlyList" or "IReadOnlyCollection" or "ISet" or "HashSet"
            or "System.Collections.Generic.List" or "System.Collections.Generic.IList"
            or "System.Collections.Generic.ICollection" or "System.Collections.Generic.IEnumerable"
            or "System.Collections.Generic.IReadOnlyList" or "System.Collections.Generic.IReadOnlyCollection"
            or "System.Collections.Generic.HashSet" or "System.Collections.Generic.ISet"))
            return false;

        var inner = typeName[(lt + 1)..gt].Trim();
        if (string.IsNullOrWhiteSpace(inner)) return false;

        elementType = inner;
        return true;
    }

    /// <summary>
    /// Returns true when <paramref name="typeName"/> is an <c>Expression&lt;Func&lt;...&gt;&gt;</c>
    /// type, either with or without the full <c>System.Linq.Expressions</c> namespace prefix.
    /// </summary>
    private static bool IsExpressionFuncTypeName(string typeName) =>
        typeName.Contains("Expression<", StringComparison.Ordinal)
        && typeName.Contains("Func<", StringComparison.Ordinal);

    /// <summary>
    /// Returns true when the <c>Func&lt;&gt;</c>'s return type is <c>bool</c> — i.e. the
    /// expression is a predicate suitable for <c>Where</c> / <c>Any</c> / <c>Count</c>.
    /// Detects by checking that the full type name ends with <c>, bool&gt;&gt;</c>
    /// (the inner <c>&gt;</c> closes <c>Func&lt;</c>, the outer closes <c>Expression&lt;</c>).
    /// </summary>
    private static bool IsBoolPredicateExpression(string typeName)
    {
        var t = typeName.TrimEnd('?');
        return t.EndsWith(", bool>>", StringComparison.Ordinal)
            || t.EndsWith(",bool>>", StringComparison.Ordinal)
            || t.EndsWith(", bool?>>", StringComparison.Ordinal);
    }

}
