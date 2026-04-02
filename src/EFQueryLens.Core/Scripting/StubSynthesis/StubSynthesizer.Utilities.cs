using System.Collections.Concurrent;
using System.Reflection;

namespace EFQueryLens.Core.Scripting.Evaluation;

internal static partial class StubSynthesizer
{
    // Cache placeholder expressions for provider-specific types — keyed by CLR type.
    // BuildScalarPlaceholderExpression is called in the retry loop so the cache is important.
    private static readonly ConcurrentDictionary<Type, string> _providerTypePlaceholderCache = new();

    private static string BuildScalarPlaceholderExpression(Type variableType)
    {
        var t = Nullable.GetUnderlyingType(variableType) ?? variableType;

        if (t == typeof(string))
            return "\"qlstub0\"";
        if (t == typeof(Guid))
            return "new System.Guid(\"00000000-0000-0000-0000-000000000001\")";
        if (t == typeof(bool))
            return "true";
        if (t == typeof(char))
            return "'a'";
        if (t == typeof(decimal))
            return "1m";
        if (t == typeof(double))
            return "1d";
        if (t == typeof(float))
            return "1f";
        if (t == typeof(long))
            return "1L";
        if (t == typeof(ulong))
            return "1UL";
        if (t == typeof(int))
            return "1";
        if (t == typeof(uint))
            return "1U";
        if (t == typeof(short))
            return "(short)1";
        if (t == typeof(ushort))
            return "(ushort)1";
        if (t == typeof(byte))
            return "(byte)1";
        if (t == typeof(sbyte))
            return "(sbyte)1";
        if (t == typeof(DateTime))
            return "System.DateTime.UnixEpoch";
        if (t.IsEnum)
            return $"({ToCSharpTypeName(t)})1";

        // Provider-specific / unknown types: try to discover a constructible placeholder via reflection.
        // This handles types like Pgvector.Vector, NetTopologySuite geometries, etc. where
        // default(T) would produce null and cause EF Core mapping issues.
        if (TryBuildReflectionPlaceholder(t, out var reflectionPlaceholder))
            return reflectionPlaceholder;

        var variableTypeName = ToCSharpTypeName(variableType);
        return $"default({variableTypeName})";
    }

    private static string BuildContainsPlaceholderValues(Type elementType)
    {
        var t = Nullable.GetUnderlyingType(elementType) ?? elementType;

        if (t == typeof(string))
            return "\"qlstub0\", \"qlstub1\"";
        if (t == typeof(Guid))
            return "System.Guid.Empty, new System.Guid(\"00000000-0000-0000-0000-000000000001\")";
        if (t == typeof(bool))
            return "false, true";
        if (t == typeof(char))
            return "'a', 'b'";
        if (t == typeof(decimal))
            return "0m, 1m";
        if (t == typeof(double))
            return "0d, 1d";
        if (t == typeof(float))
            return "0f, 1f";
        if (t == typeof(long))
            return "0L, 1L";
        if (t == typeof(ulong))
            return "0UL, 1UL";
        if (t == typeof(int))
            return "0, 1";
        if (t == typeof(uint))
            return "0U, 1U";
        if (t == typeof(short))
            return "(short)0, (short)1";
        if (t == typeof(ushort))
            return "(ushort)0, (ushort)1";
        if (t == typeof(byte))
            return "(byte)0, (byte)1";
        if (t == typeof(sbyte))
            return "(sbyte)0, (sbyte)1";
        if (t == typeof(DateTime))
            return "System.DateTime.UnixEpoch, System.DateTime.UnixEpoch.AddSeconds(1)";
        if (t.IsEnum)
        {
            var enumType = ToCSharpTypeName(t);
            return $"({enumType})0, ({enumType})1";
        }

        var first = BuildScalarPlaceholderExpression(t);
        var second = first;
        return $"{first}, {second}";
    }

    /// <summary>
    /// Attempts to build a non-null C# constructor expression for a provider-specific type
    /// by inspecting its public constructors via reflection.
    /// </summary>
    private static bool TryBuildReflectionPlaceholder(Type t, out string placeholder)
    {
        if (_providerTypePlaceholderCache.TryGetValue(t, out var cached))
        {
            placeholder = cached;
            return true;
        }

        placeholder = string.Empty;

        if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition || !t.IsClass && !t.IsValueType)
            return false;

        var typeName = ToCSharpTypeName(t);

        // 1. Parameterless public constructor: new T()
        if (t.GetConstructor(Type.EmptyTypes) is not null)
        {
            placeholder = $"new {typeName}()";
            _providerTypePlaceholderCache[t] = placeholder;
            return true;
        }

        foreach (var ctor in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                               .OrderBy(c => c.GetParameters().Length))
        {
            var ps = ctor.GetParameters();
            if (ps.Length == 0) continue;

            // 2. Single array-of-primitives parameter: new T(new float[1])
            if (ps.Length == 1 && ps[0].ParameterType.IsArray)
            {
                var elem = ps[0].ParameterType.GetElementType()!;
                if (elem.IsPrimitive || elem == typeof(decimal))
                {
                    placeholder = $"new {typeName}(new {ToCSharpTypeName(elem)}[1])";
                    _providerTypePlaceholderCache[t] = placeholder;
                    return true;
                }
            }

            // 3. All parameters are numeric primitives: new T(0, 0, 0)
            if (ps.All(p => p.ParameterType.IsPrimitive || p.ParameterType == typeof(decimal)))
            {
                var args = string.Join(", ", ps.Select(p => BuildScalarPlaceholderExpression(p.ParameterType)));
                placeholder = $"new {typeName}({args})";
                _providerTypePlaceholderCache[t] = placeholder;
                return true;
            }
        }

        return false;
    }

    private static string ToCSharpTypeName(Type t)
    {
        if (t == typeof(void)) return "void";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(byte)) return "byte";
        if (t == typeof(sbyte)) return "sbyte";
        if (t == typeof(char)) return "char";
        if (t == typeof(decimal)) return "decimal";
        if (t == typeof(double)) return "double";
        if (t == typeof(float)) return "float";
        if (t == typeof(int)) return "int";
        if (t == typeof(uint)) return "uint";
        if (t == typeof(long)) return "long";
        if (t == typeof(ulong)) return "ulong";
        if (t == typeof(object)) return "object";
        if (t == typeof(short)) return "short";
        if (t == typeof(ushort)) return "ushort";
        if (t == typeof(string)) return "string";
        if (t.IsArray) return $"{ToCSharpTypeName(t.GetElementType()!)}[]";
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
            return $"{ToCSharpTypeName(t.GetGenericArguments()[0])}?";
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition().FullName ?? t.Name;
            var tick = def.IndexOf('`');
            if (tick >= 0)
                def = def[..tick];

            return $"{def.Replace('+', '.')}<{string.Join(", ", t.GetGenericArguments().Select(ToCSharpTypeName))}>";
        }

        return (t.FullName ?? t.Name).Replace('+', '.');
    }
}
