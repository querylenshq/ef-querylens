using EFQueryLens.Core.Contracts;

namespace EFQueryLens.Lsp.Parsing;

/// <summary>
/// Builds <see cref="TranslationRequest"/> values from LSP source context so hover,
/// prewarm, and semantic cache keys all use the same inputs the daemon hashes.
/// </summary>
internal static class TranslationRequestBuilder
{
    public static TranslationRequest? TryBuild(
        string filePath,
        string sourceText,
        string expression,
        string contextVariableName,
        int line,
        int character)
    {
        if (string.IsNullOrWhiteSpace(expression) || string.IsNullOrWhiteSpace(contextVariableName))
        {
            return null;
        }

        var targetAssembly = AssemblyResolver.TryGetTargetAssembly(filePath);
        var assemblyPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(targetAssembly)
            && !targetAssembly.StartsWith("DEBUG_FAIL", StringComparison.Ordinal)
            && File.Exists(targetAssembly))
        {
            assemblyPath = targetAssembly;
        }

        var usingContext = LspSyntaxHelper.ExtractUsingContext(sourceText, filePath);
        var localVariableTypes = LspSyntaxHelper.ExtractLocalVariableTypesAtPosition(sourceText, line, character);
        var variableTypeName = LspSyntaxHelper.TryResolveDbContextTypeName(sourceText, contextVariableName, filePath);
        var factoryContextTypes = !string.IsNullOrWhiteSpace(assemblyPath)
            ? AssemblyResolver.TryExtractDbContextTypesFromFactory(assemblyPath)
            : [];
        var dbContextTypeName = ResolveDbContextTypeName(variableTypeName, factoryContextTypes);

        return new TranslationRequest
        {
            AssemblyPath = assemblyPath,
            Expression = expression,
            ContextVariableName = contextVariableName,
            DbContextTypeName = dbContextTypeName,
            AdditionalImports = usingContext.Imports.ToArray(),
            UsingAliases = new Dictionary<string, string>(usingContext.Aliases, StringComparer.Ordinal),
            UsingStaticTypes = usingContext.StaticTypes.ToArray(),
            LocalVariableTypes = localVariableTypes,
        };
    }

    public static string BuildSemanticCacheKey(TranslationRequest request)
        => TranslationCacheKey.Compute(request);

    internal static string? ResolveDbContextTypeName(
        string? variableTypeName,
        IReadOnlyList<string> factoryContextTypes)
    {
        if (string.Equals(variableTypeName, "var", StringComparison.Ordinal))
        {
            variableTypeName = null;
        }

        if (!string.IsNullOrWhiteSpace(variableTypeName))
        {
            var variableSimple = SimpleTypeName(variableTypeName);
            var factoryMatch = factoryContextTypes.FirstOrDefault(factoryType =>
                string.Equals(factoryType, variableTypeName, StringComparison.Ordinal)
                || string.Equals(SimpleTypeName(factoryType), variableSimple, StringComparison.Ordinal)
                || factoryType.EndsWith("." + variableSimple, StringComparison.Ordinal)
                || MatchesReadOnlyInterfacePair(variableTypeName, factoryType));

            if (!string.IsNullOrWhiteSpace(factoryMatch))
            {
                return factoryMatch;
            }

            return variableTypeName;
        }

        return factoryContextTypes.Count == 1 ? factoryContextTypes[0] : null;
    }

    private static bool MatchesReadOnlyInterfacePair(string variableTypeName, string factoryContextType)
    {
        var variableSimple = SimpleTypeName(variableTypeName);
        var factorySimple = SimpleTypeName(factoryContextType);
        if (!variableSimple.StartsWith("I", StringComparison.Ordinal))
        {
            return false;
        }

        var expectedConcrete = variableSimple[1..];
        return string.Equals(factorySimple, expectedConcrete, StringComparison.Ordinal);
    }

    private static string SimpleTypeName(string typeName)
    {
        var lastDot = typeName.LastIndexOf('.');
        return lastDot >= 0 ? typeName[(lastDot + 1)..] : typeName;
    }
}
