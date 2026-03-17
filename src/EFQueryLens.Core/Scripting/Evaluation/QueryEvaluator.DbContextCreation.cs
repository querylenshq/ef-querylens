using System.Reflection;
using System.Runtime.Loader;
using EFQueryLens.Core.Scripting.DesignTime;

namespace EFQueryLens.Core.Scripting.Evaluation;

public sealed partial class QueryEvaluator
{
    internal static (object Instance, string Strategy) CreateDbContextInstance(
        Type dbContextType,
        IEnumerable<Assembly> userAssemblies,
        string? executableAssemblyPath = null)
    {
        var all = AssemblyLoadContext
            .Default.Assemblies.Concat(userAssemblies)
            .ToList();

        var fromQueryLens = DesignTimeDbContextFactory.TryCreateQueryLensFactory(
            dbContextType,
            all,
            executableAssemblyPath,
            out var queryLensFailure);
        if (fromQueryLens is not null)
            return (fromQueryLens, "querylens-factory");

        var details = string.Join(" ", new[] { queryLensFailure }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        var executableHint = string.IsNullOrWhiteSpace(executableAssemblyPath)
            ? "Use the compiled executable assembly (API / Worker / Console) as the QueryLens target."
            : $"Selected executable assembly: '{Path.GetFileName(executableAssemblyPath)}'.";

        throw new InvalidOperationException(
            $"No factory found for '{dbContextType.FullName}'. " +
            "Add an IQueryLensDbContextFactory<T> implementation to your executable project (API / Worker / Console), not in a class library. " +
            executableHint + " " +
            "See the QueryLens README for setup instructions." +
            (string.IsNullOrWhiteSpace(details) ? string.Empty : $" Details: {details}"));
    }
}
