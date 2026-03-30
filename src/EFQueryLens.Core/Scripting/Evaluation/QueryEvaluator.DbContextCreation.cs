using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
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

    internal static (object Instance, string Strategy) CreateDbContextInstance(
        Type dbContextType,
        IEnumerable<Assembly> userAssemblies,
        string? executableAssemblyPath = null)
    {
        var all = AssemblyLoadContext
            .Default.Assemblies.Concat(userAssemblies)
            .ToList();

        // Set up fake services before calling factory to enable named connection string resolution
        _fakeServiceProvider.Value = new QueryLensFakeServiceProvider();

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
            _fakeServiceProvider.Value = null;
        }
    }

    /// <summary>
    /// Provides fake services for DbContext construction when real services are unavailable.
    /// This ensures DbContext.OnConfiguring can resolve dependencies like IConfiguration
    /// for named connection strings without failing in the offline evaluation context.
    /// </summary>
    internal sealed class QueryLensFakeServiceProvider : IServiceProvider
    {
        private readonly IConfiguration _configuration = new QueryLensFakeConfiguration();

        public object? GetService(Type serviceType)
        {
            // Provide fake implementations for common services EF Core might request
            if (serviceType == typeof(IConfiguration))
                return _configuration;

            if (serviceType == typeof(IConfigurationRoot))
                return _configuration as IConfigurationRoot;

            // For other IConfiguration-like types, return our configuration
            if (serviceType?.Name is "IConfiguration" or "IConfigurationRoot")
                return _configuration;

            // Return null for unknown services; EF Core will use defaults
            return null;
        }
    }

    /// <summary>
    /// Fake IConfiguration that provides dummy connection strings for any "Name=..." lookup.
    /// When EF Core encounters named connection strings like UseSqlServer("Name=MainConnection"),
    /// it will resolve them through this configuration instead of failing.
    /// </summary>
    internal sealed class QueryLensFakeConfiguration : IConfiguration, IConfigurationRoot
    {
        private readonly IConfigurationSection _nullSection =
            new QueryLensFakeConfigurationSection();

        public string? this[string key]
        {
            get
            {
                // Return dummy connection strings for connection string lookups
                if (key?.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase) == true)
                    return "Server=localhost;Database=__querylens__;Encrypt=false;TrustServerCertificate=true;";

                // Return null for other keys so EF Core uses its defaults
                return null;
            }
            set { }
        }

        public IEnumerable<IConfigurationProvider> Providers => [];

        public IConfigurationSection GetSection(string key) =>
            _nullSection;

        public IEnumerable<IConfigurationSection> GetChildren() =>
            [];

        public IChangeToken GetReloadToken() =>
            new QueryLensChangeToken();

        public void Reload() { }
    }

    /// <summary>
    /// Fake configuration section that acts as a null/empty fallback section.
    /// </summary>
    internal sealed class QueryLensFakeConfigurationSection : IConfigurationSection
    {
        public string Key => string.Empty;
        public string Path => string.Empty;
        public string? Value { get; set; }

        public string? this[string key]
        {
            get => null;
            set { }
        }

        public IEnumerable<IConfigurationSection> GetChildren() =>
            [];

        public IChangeToken GetReloadToken() =>
            new QueryLensChangeToken();

        public IConfigurationSection GetSection(string key) =>
            this;
    }

    /// <summary>
    /// Fake change token that never signals changes (configuration is static in eval context).
    /// </summary>
    internal sealed class QueryLensChangeToken : IChangeToken
    {
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;

        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) =>
            new NoOpDisposable();

        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
