using System.Collections.Concurrent;
using System.Reflection;
using EFQueryLens.Core.Contracts;

namespace EFQueryLens.Core.Scripting.Evaluation;

public sealed partial class QueryEvaluator
{
    private static readonly ConcurrentDictionary<string, (string ProviderName, string EfCoreVersion)>
        SProviderMetadataCache = new(StringComparer.Ordinal);

    private static bool ShouldWarnExpressionPartialRisk(
        string expression,
        IReadOnlyList<QuerySqlCommand> commands)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        // Only warn when the query shape can genuinely produce child-collection SQL commands
        // that the offline interceptor might not capture.  The two real triggers are:
        //
        //   1. EF actually emitted more than one SQL command (split-query or multi-table
        //      materialisation that we already captured — warn to note the preview shows all).
        //   2. The expression contains .Include(, which is the main driver of split queries /
        //      extra round-trips for collection navigations.
        //
        // A plain .Select(c => c.Name).ToList() on a scalar projection never generates a
        // second command and should not raise this warning.
        if (commands.Count > 1)
            return true;

        var hasSelect = expression.Contains(".Select(", StringComparison.OrdinalIgnoreCase);
        var hasInclude = expression.Contains(".Include(", StringComparison.OrdinalIgnoreCase)
                      || expression.Contains(".ThenInclude(", StringComparison.OrdinalIgnoreCase);

        if (!hasSelect || !hasInclude)
            return false;

        return expression.Contains(".ToList(", StringComparison.OrdinalIgnoreCase)
            || expression.Contains(".ToArray(", StringComparison.OrdinalIgnoreCase)
            || expression.Contains(".ToDictionary(", StringComparison.OrdinalIgnoreCase)
            || expression.Contains(".ToLookup(", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddWarningIfMissing(List<QueryWarning> warnings, QueryWarning warning)
    {
        if (warnings.Any(w => string.Equals(w.Code, warning.Code, StringComparison.OrdinalIgnoreCase)))
            return;

        warnings.Add(warning);
    }

    private static QueryTranslationResult Failure(
        string message,
        TimeSpan elapsed,
        Type? dbContextType,
        IEnumerable<Assembly>? userAssemblies) =>
        new()
        {
            Success = false,
            ErrorMessage = message,
            Metadata = BuildMetadata(dbContextType, userAssemblies, elapsed),
        };

    private static TranslationMetadata BuildMetadata(
        Type? dbContextType,
        IEnumerable<Assembly>? userAssemblies,
        TimeSpan elapsed,
        string creationStrategy = "unknown",
        EvaluationStageTimings? stageTimings = null)
    {
        var (providerName, efCoreVersion) = GetProviderMetadata(userAssemblies);

        return new TranslationMetadata
        {
            DbContextType = dbContextType?.FullName ?? "unknown",
            ProviderName = providerName,
            EfCoreVersion = efCoreVersion,
            TranslationTime = elapsed,
            CreationStrategy = creationStrategy,
            ContextResolutionTime = stageTimings?.ContextResolution,
            DbContextCreationTime = stageTimings?.DbContextCreation,
            MetadataReferenceBuildTime = stageTimings?.MetadataReferenceBuild,
            RoslynCompilationTime = stageTimings?.RoslynCompilation,
            CompilationRetryCount = stageTimings?.CompilationRetryCount,
            EvalAssemblyLoadTime = stageTimings?.EvalAssemblyLoad,
            RunnerExecutionTime = stageTimings?.RunnerExecution,
        };
    }

    private static (string ProviderName, string EfCoreVersion) GetProviderMetadata(IEnumerable<Assembly>? userAssemblies)
    {
        if (userAssemblies is null)
            return ("unknown", "unknown");

        var assemblyList = userAssemblies.ToList();
        if (assemblyList.Count == 0)
            return ("unknown", "unknown");

        var cacheKey = ComputeAssemblySetHash(assemblyList);
        return SProviderMetadataCache.GetOrAdd(cacheKey, static (_, assemblies) =>
        {
            var provider = DetectProviderName(assemblies);
            var efCoreVersion = GetEfCoreVersion(assemblies);
            return (provider, efCoreVersion);
        }, assemblyList);
    }

    private static string DetectProviderName(IReadOnlyCollection<Assembly> assemblies)
    {
        var loadedAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asm in assemblies)
        {
            var name = asm.GetName().Name;
            if (name is null)
                continue;

            loadedAssemblyNames.Add(name);
        }

        // Deterministic priority avoids enumeration-order drift when multiple providers are loaded.
        if (loadedAssemblyNames.Any(name => name.StartsWith("Pomelo.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)))
            return "Pomelo.EntityFrameworkCore.MySql";
        if (loadedAssemblyNames.Any(name => name.StartsWith("Npgsql.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)))
            return "Npgsql.EntityFrameworkCore.PostgreSQL";
        if (loadedAssemblyNames.Contains("Microsoft.EntityFrameworkCore.SqlServer"))
            return "Microsoft.EntityFrameworkCore.SqlServer";
        if (loadedAssemblyNames.Contains("Microsoft.EntityFrameworkCore.Sqlite"))
            return "Microsoft.EntityFrameworkCore.Sqlite";
        if (loadedAssemblyNames.Contains("Microsoft.EntityFrameworkCore.InMemory"))
            return "Microsoft.EntityFrameworkCore.InMemory";

        return "unknown";
    }

    private static string GetEfCoreVersion(IReadOnlyCollection<Assembly> assemblies)
    {
        return assemblies.FirstOrDefault(a => a.GetName().Name == "Microsoft.EntityFrameworkCore")
            ?.GetName().Version?.ToString() ?? "unknown";
    }
}
