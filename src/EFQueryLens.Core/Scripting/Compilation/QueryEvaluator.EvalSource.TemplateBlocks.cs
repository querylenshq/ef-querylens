using System.Text;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Core.Scripting.Compilation;

namespace EFQueryLens.Core.Scripting.Evaluation;

public sealed partial class QueryEvaluator
{
    private static void AppendBaseUsings(StringBuilder sb)
    {
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Collections;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Data;");
        sb.AppendLine("using System.Data.Common;");
        sb.AppendLine("using System.Globalization;");
        sb.AppendLine("using System.Reflection;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
    }

    private static void AppendRequestUsings(
        StringBuilder sb,
        TranslationRequest request,
        IReadOnlySet<string> knownNamespaces,
        IReadOnlySet<string> knownTypes,
        IReadOnlyCollection<string> synthesizedUsingStaticTypes,
        IReadOnlyCollection<string> synthesizedUsingNamespaces,
        IReadOnlyDictionary<string, string> synthesizedUsingAliases)
    {
        foreach (var import in request.AdditionalImports)
        {
            if (IsValidUsingName(import))
                sb.AppendLine($"using {import};");
        }

        foreach (var ns in synthesizedUsingNamespaces
                     .Where(n => IsValidUsingName(n) && IsResolvableNamespace(n, knownNamespaces))
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            sb.AppendLine($"using {ns};");
        }

        foreach (var kvp in request.UsingAliases
                     .Where(kvp => IsValidAliasName(kvp.Key) && IsValidUsingName(kvp.Value))
                     .OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"using {kvp.Key} = {kvp.Value};");
        }

        foreach (var kvp in synthesizedUsingAliases
                     .Where(kvp => IsValidAliasName(kvp.Key)
                                   && IsValidUsingName(kvp.Value)
                                   && !request.UsingAliases.ContainsKey(kvp.Key))
                     .OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"using {kvp.Key} = {kvp.Value};");
        }

        foreach (var st in request.UsingStaticTypes
                     .Where(IsValidUsingName)
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            sb.AppendLine($"using static {st};");
        }

        foreach (var st in synthesizedUsingStaticTypes
                     .Where(IsValidUsingName)
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            sb.AppendLine($"using static {st};");
        }
    }

    private static void AppendRunner(
        StringBuilder sb,
        Type dbContextType,
        TranslationRequest request,
        IReadOnlyList<string> stubs)
    {
        sb.AppendLine();

        var contextDeclaration =
            $"        var {request.ContextVariableName} = ({dbContextType.FullName!.Replace('+', '.')})(object)__ctx__;";

        var mergedStubs = MergeLocalAndRetryStubs(dbContextType, request, stubs);
        var stubsBlock = mergedStubs.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, mergedStubs.Select(static stub => $"        {stub}"));

        var renderedRunner = EvalSourceTemplateCatalog.Render(
            EvalSourceTemplateCatalog.Runner,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["__QL_CONTEXT_DECL__"] = contextDeclaration,
                ["__QL_STUBS__"] = stubsBlock,
                ["__QL_CONTEXT_VAR__"] = request.ContextVariableName,
                ["__QL_EXPRESSION__"] = request.Expression,
            });

        sb.Append(renderedRunner);
    }

    private static List<string> MergeLocalAndRetryStubs(
        Type dbContextType,
        TranslationRequest request,
        IReadOnlyList<string> stubs)
    {
        var merged = new List<string>();
        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var kv in request.LocalVariableTypes.OrderBy(static x => x.Key, StringComparer.Ordinal))
        {
            if (string.Equals(kv.Key, request.ContextVariableName, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(kv.Value))
            {
                continue;
            }

            var stub = BuildStubFromTypeName(kv.Value, kv.Key, dbContextType)
                       ?? BuildStubDeclaration(kv.Key, null, request, dbContextType);
            if (TryExtractStubVariableName(stub, out var name) && declared.Add(name))
            {
                merged.Add(stub);
            }
        }

        foreach (var stub in stubs)
        {
            if (TryExtractStubVariableName(stub, out var name) && declared.Add(name))
            {
                merged.Add(stub);
            }
        }

        return merged;
    }

    private static bool TryExtractStubVariableName(string stub, out string variableName)
    {
        variableName = string.Empty;
        var match = System.Text.RegularExpressions.Regex.Match(
            stub,
            @"^\s*(.+?)\s+([A-Za-z_][A-Za-z0-9_]*)\s*=",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        variableName = match.Groups[2].Value;
        return !string.IsNullOrWhiteSpace(variableName);
    }

    private static void AppendFallbackExtensions(StringBuilder sb, bool includeGridifyFallbackExtensions)
    {
        if (!includeGridifyFallbackExtensions)
            return;

        sb.AppendLine();
        sb.AppendLine("internal static class __QueryLensGridifyFallbackExtensions__");
        sb.AppendLine("{");
        sb.AppendLine("    public static System.Linq.IQueryable<T> ApplyFilteringAndOrdering<T>(");
        sb.AppendLine("        this System.Linq.IQueryable<T> source,");
        sb.AppendLine("        object? query) => source;");
        sb.AppendLine();
        sb.AppendLine("    public static System.Linq.IQueryable<T> ApplyFilteringAndOrdering<T>(");
        sb.AppendLine("        this System.Linq.IQueryable<T> source,");
        sb.AppendLine("        object? query,");
        sb.AppendLine("        object? mapper) => source;");
        sb.AppendLine();
        sb.AppendLine("    public static System.Linq.IQueryable<T> ApplyPaging<T>(");
        sb.AppendLine("        this System.Linq.IQueryable<T> source,");
        sb.AppendLine("        int page,");
        sb.AppendLine("        int pageSize) => source;");
        sb.AppendLine("}");
    }
}
