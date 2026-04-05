using System;
using EFQueryLens.Core.Contracts;

namespace EFQueryLens.Core.Scripting.Compilation;

/// <summary>
/// V2 capture-plan support for EvalSourceBuilder.
/// Interprets v2 capture-plan policies (ReplayInitializer, UsePlaceholder, Reject)
/// to drive code generation for symbol initialization.
/// </summary>
internal static partial class EvalSourceBuilder
{
    /// <summary>
    /// Builds initialization code for a v2 capture-plan entry based on capture policy.
    /// </summary>
    /// <para>
    /// Policy interpretation:
    /// - ReplayInitializer: Emit normal replay initialization code (status quo)
    /// - UsePlaceholder: Emit default/placeholder value for the symbol type
    /// - Reject: Symbol should not be initialized; capture will fail
    /// </para>
    internal static string? BuildV2CaptureInitializationCode(V2CapturePlanEntry entry)
    {
        return entry.CapturePolicy switch
        {
            LocalSymbolReplayPolicies.ReplayInitializer 
                => BuildReplayInitializerCode(entry),
            
            LocalSymbolReplayPolicies.UsePlaceholder 
                => BuildPlaceholderInitializationCode(entry),
            
            LocalSymbolReplayPolicies.Reject 
                => null,
            
            _ => null,
        };
    }

    /// <summary>
    /// Builds standard replay initializer code for a symbol.
    /// This is the current generation path for symbols that can be captured.
    /// </summary>
    private static string BuildReplayInitializerCode(V2CapturePlanEntry entry)
    {
        // Use the entry's InitializerExpression if provided, otherwise fall back to default
        if (!string.IsNullOrWhiteSpace(entry.InitializerExpression))
        {
            return $"var {entry.Name} = {entry.InitializerExpression};";
        }

        // Fallback: default(T) for the symbol's type
        return $"var {entry.Name} = default({entry.TypeName});";
    }

    /// <summary>
    /// Builds placeholder initialization code for a symbol.
    /// Emits a deterministic placeholder value for the symbol type.
    /// If placeholder generation fails for unsupported types, emits a diagnostic and falls back to default(T).
    /// </summary>
    private static string BuildPlaceholderInitializationCode(V2CapturePlanEntry entry)
    {
        if (TryBuildCollectionPlaceholder(entry.TypeName, out var collectionPlaceholder))
        {
            return $"var {entry.Name} = {collectionPlaceholder};";
        }

        if (TryBuildScalarPlaceholder(entry.TypeName, out var scalarPlaceholder))
        {
            return $"var {entry.Name} = {scalarPlaceholder};";
        }

        // Unsupported type: emit diagnostic and fall back to default(T).
        var diagnostic = EvalSourceBuilderDiagnostics.DetailedPlaceholderUnsupported(
            entry.Name,
            entry.TypeName ?? "unknown");
        EvalSourceBuilderDiagnosticContextHolder.Current.Emit(diagnostic);

        return $"var {entry.Name} = default({entry.TypeName});";
    }

    private static bool TryBuildCollectionPlaceholder(string? typeName, out string placeholder)
    {
        placeholder = string.Empty;
        var normalized = NormalizeTypeName(typeName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (TryExtractArrayElementType(normalized, out var arrayElementType))
        {
            var (first, second) = BuildTwoSeedValues(arrayElementType);
            placeholder = $"new {arrayElementType}[] {{ {first}, {second} }}";
            return true;
        }

        if (TryExtractGenericElementType(normalized, "List", out var listElementType)
            || TryExtractGenericElementType(normalized, "System.Collections.Generic.List", out listElementType))
        {
            var (first, second) = BuildTwoSeedValues(listElementType);
            placeholder =
                $"new global::System.Collections.Generic.List<{listElementType}> {{ {first}, {second} }}";
            return true;
        }

        if (TryExtractGenericElementType(normalized, "IEnumerable", out var enumerableElementType)
            || TryExtractGenericElementType(normalized, "System.Collections.Generic.IEnumerable", out enumerableElementType))
        {
            var (first, second) = BuildTwoSeedValues(enumerableElementType);
            placeholder = $"new {enumerableElementType}[] {{ {first}, {second} }}";
            return true;
        }

        if (TryExtractGenericElementType(normalized, "ISet", out var setElementType)
            || TryExtractGenericElementType(normalized, "System.Collections.Generic.ISet", out setElementType)
            || TryExtractGenericElementType(normalized, "HashSet", out setElementType)
            || TryExtractGenericElementType(normalized, "System.Collections.Generic.HashSet", out setElementType))
        {
            var (first, second) = BuildTwoSeedValues(setElementType);
            placeholder =
                $"new global::System.Collections.Generic.HashSet<{setElementType}> {{ {first}, {second} }}";
            return true;
        }

        return false;
    }

    private static bool TryBuildScalarPlaceholder(string? typeName, out string placeholder)
    {
        placeholder = string.Empty;
        var normalized = NormalizeTypeName(typeName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var isNullable = normalized.EndsWith("?", StringComparison.Ordinal);
        var coreType = isNullable ? normalized[..^1] : normalized;

        switch (coreType)
        {
            case "string":
            case "String":
            case "System.String":
                placeholder = "\"qlstub0\"";
                return true;

            case "bool":
            case "Boolean":
            case "System.Boolean":
                placeholder = "true";
                return true;

            case "byte":
            case "Byte":
            case "System.Byte":
                placeholder = "1";
                return true;

            case "short":
            case "Int16":
            case "System.Int16":
                placeholder = "1";
                return true;

            case "int":
            case "Int32":
            case "System.Int32":
                placeholder = "1";
                return true;

            case "long":
            case "Int64":
            case "System.Int64":
                placeholder = "1L";
                return true;

            case "float":
            case "Single":
            case "System.Single":
                placeholder = "1.0f";
                return true;

            case "double":
            case "Double":
            case "System.Double":
                placeholder = "1.0d";
                return true;

            case "decimal":
            case "Decimal":
            case "System.Decimal":
                placeholder = "1.0m";
                return true;

            case "Guid":
            case "System.Guid":
                placeholder = "global::System.Guid.Parse(\"11111111-1111-1111-1111-111111111111\")";
                return true;

            case "DateTime":
            case "System.DateTime":
                placeholder = "global::System.DateTime.UtcNow";
                return true;

            case "DateOnly":
            case "System.DateOnly":
                placeholder = "global::System.DateOnly.FromDateTime(global::System.DateTime.UtcNow)";
                return true;

            case "TimeOnly":
            case "System.TimeOnly":
                placeholder = "global::System.TimeOnly.FromDateTime(global::System.DateTime.UtcNow)";
                return true;

            default:
                return false;
        }
    }

    private static (string First, string Second) BuildTwoSeedValues(string elementTypeName)
    {
        var normalizedElementType = NormalizeTypeName(elementTypeName);

        if (IsStringTypeName(normalizedElementType))
        {
            return ("\"qlstub0\"", "\"qlstub1\"");
        }

        return normalizedElementType switch
        {
            "bool" or "Boolean" or "System.Boolean" => ("true", "false"),
            "byte" or "Byte" or "System.Byte" => ("1", "2"),
            "short" or "Int16" or "System.Int16" => ("1", "2"),
            "int" or "Int32" or "System.Int32" => ("1", "2"),
            "long" or "Int64" or "System.Int64" => ("1L", "2L"),
            "float" or "Single" or "System.Single" => ("1.0f", "2.0f"),
            "double" or "Double" or "System.Double" => ("1.0d", "2.0d"),
            "decimal" or "Decimal" or "System.Decimal" => ("1.0m", "2.0m"),
            "Guid" or "System.Guid"
                =>
                    (
                        "global::System.Guid.Parse(\"11111111-1111-1111-1111-111111111111\")",
                        "global::System.Guid.Parse(\"22222222-2222-2222-2222-222222222222\")"
                    ),
            "DateTime" or "System.DateTime"
                =>
                    (
                        "global::System.DateTime.UtcNow",
                        "global::System.DateTime.UtcNow.AddDays(1)"
                    ),
            "DateOnly" or "System.DateOnly"
                =>
                    (
                        "global::System.DateOnly.FromDateTime(global::System.DateTime.UtcNow)",
                        "global::System.DateOnly.FromDateTime(global::System.DateTime.UtcNow.AddDays(1))"
                    ),
            "TimeOnly" or "System.TimeOnly"
                =>
                    (
                        "global::System.TimeOnly.FromDateTime(global::System.DateTime.UtcNow)",
                        "global::System.TimeOnly.FromDateTime(global::System.DateTime.UtcNow.AddMinutes(1))"
                    ),
            _ => ($"default({elementTypeName})", $"default({elementTypeName})"),
        };
    }

    private static bool TryExtractArrayElementType(string normalizedTypeName, out string elementTypeName)
    {
        elementTypeName = string.Empty;
        if (!normalizedTypeName.EndsWith("[]", StringComparison.Ordinal))
        {
            return false;
        }

        elementTypeName = normalizedTypeName[..^2].Trim();
        return !string.IsNullOrWhiteSpace(elementTypeName);
    }

    private static bool TryExtractGenericElementType(
        string normalizedTypeName,
        string genericTypeName,
        out string elementTypeName)
    {
        elementTypeName = string.Empty;
        var prefix = genericTypeName + "<";
        if (!normalizedTypeName.StartsWith(prefix, StringComparison.Ordinal)
            || !normalizedTypeName.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        elementTypeName = normalizedTypeName[prefix.Length..^1].Trim();
        return !string.IsNullOrWhiteSpace(elementTypeName);
    }

    private static string NormalizeTypeName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return string.Empty;
        }

        return typeName.Replace("global::", string.Empty, StringComparison.Ordinal).Trim();
    }

    private static bool IsStringTypeName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        var normalized = NormalizeTypeName(typeName);
        return string.Equals(normalized, "string", StringComparison.Ordinal)
               || string.Equals(normalized, "string?", StringComparison.Ordinal)
               || string.Equals(normalized, "String", StringComparison.Ordinal)
               || string.Equals(normalized, "String?", StringComparison.Ordinal)
               || string.Equals(normalized, "System.String", StringComparison.Ordinal)
               || string.Equals(normalized, "System.String?", StringComparison.Ordinal);
    }
}
