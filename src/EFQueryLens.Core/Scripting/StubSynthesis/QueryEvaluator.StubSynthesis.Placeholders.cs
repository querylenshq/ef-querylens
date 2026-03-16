using System.Text.RegularExpressions;

namespace EFQueryLens.Core.Scripting;

public sealed partial class QueryEvaluator
{
    private static bool LooksLikeCancellationTokenArgument(string v, string expr)
    {
        if (Regex.IsMatch(expr, $@"\w+Async\s*\([^\)]*\b{Regex.Escape(v)}\b[^\)]*\)")) return true;
        return v.Equals("ct", StringComparison.OrdinalIgnoreCase)
            || v.Equals("cancellationToken", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeBooleanConditionIdentifier(string variableName, string expression)
    {
        var id = Regex.Escape(variableName);

        // Handles conditions like: isIntranetUser || ...  and  ... && isIntranetUser
        if (Regex.IsMatch(expression, $@"(?<!\w){id}(?!\w)\s*(\|\||&&)"))
            return true;

        if (Regex.IsMatch(expression, $@"(\|\||&&)\s*!?\s*(?<!\w){id}(?!\w)"))
            return true;

        // Handles unary negation: !isIntranetUser
        if (Regex.IsMatch(expression, $@"!\s*(?<!\w){id}(?!\w)"))
            return true;

        // Handles ternary condition usage: isIntranetUser ? x : y
        if (Regex.IsMatch(expression, $@"(?<!\w){id}(?!\w)\s*\?"))
            return true;

        return false;
    }

    private static bool LooksLikeNumericArithmeticIdentifier(string variableName, string expression)
    {
        var id = Regex.Escape(variableName);

        if (Regex.IsMatch(expression, $@"(?<!\w){id}(?!\w)\s*[*+\-/]"))
            return true;

        if (Regex.IsMatch(expression, $@"[*+\-/]\s*(?<!\w){id}(?!\w)"))
            return true;

        if (Regex.IsMatch(expression, $@"\.(Skip|Take)\s*\(\s*(?<!\w){id}(?!\w)\s*\)", RegexOptions.IgnoreCase))
            return true;

        return false;
    }

    private static string BuildContainsPlaceholderValues(Type elementType)
    {
        var t = Nullable.GetUnderlyingType(elementType) ?? elementType;

        if (t == typeof(Guid))
            return "System.Guid.Empty, new System.Guid(\"00000000-0000-0000-0000-000000000001\")";
        if (t == typeof(string))
            return "\"__ql_stub_0\", \"__ql_stub_1\"";
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
            return "System.DateTime.UnixEpoch, System.DateTime.UnixEpoch.AddDays(1)";
        if (t.IsEnum)
        {
            var enumTypeName = ToCSharpTypeName(t);
            return $"({enumTypeName})0, ({enumTypeName})1";
        }

        var typeName = ToCSharpTypeName(elementType);
        return $"default({typeName})!, default({typeName})!";
    }

    private static string BuildScalarPlaceholderExpression(Type variableType)
    {
        var t = Nullable.GetUnderlyingType(variableType) ?? variableType;

        if (t == typeof(string))
            return "\"__ql_stub_0\"";
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

        var variableTypeName = ToCSharpTypeName(variableType);
        return $"default({variableTypeName})";
    }

    private static Type? FindEntityPropertyType(Type ctx, string propName)
    {
        foreach (var p in ctx.GetProperties())
        {
            if (!p.PropertyType.IsGenericType) continue;
            var ep = p.PropertyType.GetGenericArguments().FirstOrDefault()?.GetProperty(propName);
            if (ep is not null) return ep.PropertyType;
        }

        return null;
    }

    private static string ToCSharpTypeName(Type t)
    {
        if (t == typeof(void))    return "void";
        if (t == typeof(bool))    return "bool";
        if (t == typeof(byte))    return "byte";
        if (t == typeof(sbyte))   return "sbyte";
        if (t == typeof(char))    return "char";
        if (t == typeof(decimal)) return "decimal";
        if (t == typeof(double))  return "double";
        if (t == typeof(float))   return "float";
        if (t == typeof(int))     return "int";
        if (t == typeof(uint))    return "uint";
        if (t == typeof(long))    return "long";
        if (t == typeof(ulong))   return "ulong";
        if (t == typeof(object))  return "object";
        if (t == typeof(short))   return "short";
        if (t == typeof(ushort))  return "ushort";
        if (t == typeof(string))  return "string";
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
