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

        var fromEfDesignTime = DesignTimeDbContextFactory.TryCreateEfDesignTimeFactory(
            dbContextType,
            all,
            executableAssemblyPath,
            out var efDesignTimeFailure);
        if (fromEfDesignTime is not null)
            return (fromEfDesignTime, "ef-design-time-factory");

        var executableHint = string.IsNullOrWhiteSpace(executableAssemblyPath)
            ? "Use the compiled executable assembly (API / Worker / Console) as the QueryLens target."
            : $"Selected executable assembly: '{Path.GetFileName(executableAssemblyPath)}'.";

        throw new InvalidOperationException(
            $"No factory found for '{dbContextType.FullName}'. " +
            "Add an IDesignTimeDbContextFactory<T> implementation to your executable project (API / Worker / Console), not in a class library. " +
            "This is the same factory EF Core uses for 'dotnet ef migrations add'. " +
            executableHint + " " +
            "See: https://learn.microsoft.com/en-us/ef/core/cli/dbcontext-creation" +
            (string.IsNullOrWhiteSpace(efDesignTimeFailure) ? string.Empty : $" Details: {efDesignTimeFailure}"));
    }
}
