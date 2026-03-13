using System.Reflection;

namespace EFQueryLens.Core;

internal interface IDbContextLease : IAsyncDisposable
{
    object Instance { get; }

    string PoolKey { get; }

    string Strategy { get; }
}

/// <summary>
/// Provides pooled DbContext instances to avoid recreation costs.
/// Implementations should support warm reuse of DbContext across queries.
/// </summary>
internal interface IDbContextPoolProvider
{
    /// <summary>
    /// Acquires an exclusive lease for a DbContext instance.
    /// Each lease owns one context instance until disposed.
    /// </summary>
    ValueTask<IDbContextLease> AcquireDbContextLeaseAsync(
        Type dbContextType,
        string assemblyPath,
        IEnumerable<Assembly> userAssemblies,
        CancellationToken cancellationToken = default);
}
