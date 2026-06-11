using System.Reflection;
using EFQueryLens.Core.AssemblyContext;
using EFQueryLens.Core.Scripting.DesignTime;

namespace EFQueryLens.Core.Scripting.Evaluation;

public sealed partial class QueryEvaluator
{
    internal static (object Instance, string Strategy) CreateDbContextInstance(
        Type dbContextType,
        IEnumerable<Assembly> userAssemblies,
        string? executableAssemblyPath = null,
        ProjectAssemblyContext? assemblyContext = null)
    {
        var userAssemblyList = userAssemblies as IReadOnlyList<Assembly> ?? userAssemblies.ToList();

        var fromQueryLens = TryCreateQueryLensFactoryWithStage2Fallback(
            dbContextType,
            userAssemblyList,
            executableAssemblyPath,
            assemblyContext,
            out var queryLensFailure);
        if (fromQueryLens is not null)
            return (fromQueryLens, "querylens-factory");

        var executableHint = string.IsNullOrWhiteSpace(executableAssemblyPath)
            ? "Use the compiled executable assembly (API / Worker / Console) as the QueryLens target."
            : $"Selected executable assembly: '{Path.GetFileName(executableAssemblyPath)}'.";

        throw new InvalidOperationException(
            $"No IQueryLensDbContextFactory<{dbContextType.Name}> found. " +
            "Add an IQueryLensDbContextFactory<T> implementation to your executable project (API / Worker / Console), not in a class library. " +
            executableHint +
            (string.IsNullOrWhiteSpace(queryLensFailure) ? string.Empty : $" Details: {queryLensFailure}"));
    }

    private static object? TryCreateQueryLensFactoryWithStage2Fallback(
        Type dbContextType,
        IReadOnlyList<Assembly> userAssemblies,
        string? executableAssemblyPath,
        ProjectAssemblyContext? assemblyContext,
        out string? failureReason)
    {
        var fromQueryLens = DesignTimeDbContextFactory.TryCreateQueryLensFactory(
            dbContextType,
            userAssemblies,
            executableAssemblyPath,
            out failureReason);
        if (fromQueryLens is not null || assemblyContext is null)
        {
            return fromQueryLens;
        }

        TryLoadSiblingAssemblies(assemblyContext);
        return DesignTimeDbContextFactory.TryCreateQueryLensFactory(
            dbContextType,
            assemblyContext.LoadedAssemblies.ToList(),
            executableAssemblyPath,
            out failureReason);
    }
}
