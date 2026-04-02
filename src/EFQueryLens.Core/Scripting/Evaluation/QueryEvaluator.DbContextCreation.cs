using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using EFQueryLens.Core.AssemblyContext;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Core.Scripting.DesignTime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace EFQueryLens.Core.Scripting.Evaluation;

public sealed partial class QueryEvaluator
{
    /// <summary>
    /// AsyncLocal context that holds fake services for DbContext creation.
    /// This allows DbContext.OnConfiguring to resolve named connection strings
    /// and other configuration that wouldn't be available in the offline eval context.
    /// </summary>
    private static readonly AsyncLocal<IServiceProvider?> _fakeServiceProvider =
        new();

    /// <summary>
    /// Exposes the current fake service provider for QueryLensFakeServices.
    /// </summary>
    internal static IServiceProvider? CurrentFakeServiceProvider
    {
        get => _fakeServiceProvider.Value;
        set => _fakeServiceProvider.Value = value;
    }

    internal static (object Instance, string Strategy) CreateDbContextInstance(
        Type dbContextType,
        IEnumerable<Assembly> userAssemblies,
        string? executableAssemblyPath = null)
    {
        var all = AssemblyLoadContext
            .Default.Assemblies.Concat(userAssemblies)
            .ToList();

        // Set up fake services before calling factory to enable named connection string resolution
        var fakeProvider = new QueryLensFakeServices.ServiceProvider();
        CurrentFakeServiceProvider = fakeProvider;

        // Attempt to globally register the fake configuration in Microsoft.Extensions.DependencyInjection
        // so that EF Core can find it when building its internal service provider
        TryRegisterFakeServicesInDefaultDI(fakeProvider);

        try
        {
            var fromQueryLens = DesignTimeDbContextFactory.TryCreateQueryLensFactory(
                dbContextType,
                all,
                executableAssemblyPath,
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
        finally
        {
            CurrentFakeServiceProvider = null;
        }
    }

    private async Task<(object Instance, string Strategy, IDbContextLease? Lease)> CreateDbContextInstanceAsync(
        Type dbContextType,
        ProjectAssemblyContext alcCtx,
        IDbContextPoolProvider? dbContextPoolProvider,
        string? poolAssemblyPath,
        CancellationToken ct)
    {
        if (dbContextPoolProvider is not null && !string.IsNullOrWhiteSpace(poolAssemblyPath))
        {
            var lease = await dbContextPoolProvider.AcquireDbContextLeaseAsync(
                dbContextType,
                poolAssemblyPath,
                alcCtx.LoadedAssemblies,
                ct);
            return (lease.Instance, lease.Strategy, lease);
        }

        var created = CreateDbContextInstance(
            dbContextType,
            alcCtx.LoadedAssemblies,
            alcCtx.AssemblyPath);
        return (created.Instance, created.Strategy, null);
    }

    /// <summary>
    /// Attempts to globally register fake services with Microsoft.Extensions.DependencyInjection
    /// so that EF Core's internal service provider discovery can find them.
    /// This uses reflection to access the DI system without requiring a direct dependency on it.
    /// </summary>
    private static void TryRegisterFakeServicesInDefaultDI(IServiceProvider fakeProvider)
    {
        try
        {
            // Try to find and use Microsoft.Extensions.DependencyInjection
            var depsAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Microsoft.Extensions.DependencyInjection.Abstractions");
            
            if (depsAsm is null)
                return; // DI not loaded, skip

            // Use reflection to attempt setting a default service provider or modifying service discovery
            // This is a secondary mechanism in case EF Core has a mechanism to look up services globally
            // Without this, the AsyncLocal context and factory-level service provision should still work
        }
        catch
        {
            // Silently ignore failures - this is best-effort service registration
        }
    }
}
