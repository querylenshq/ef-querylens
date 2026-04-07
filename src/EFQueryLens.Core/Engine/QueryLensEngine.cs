using System.Reflection;
using EFQueryLens.Core.AssemblyContext;
using EFQueryLens.Core.Common;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Core.Scripting;
using EFQueryLens.Core.Scripting.Evaluation;
using QueryEvaluator = EFQueryLens.Core.Scripting.Evaluation.QueryEvaluator;

namespace EFQueryLens.Core.Engine;

/// <summary>
/// Default implementation of <see cref="IQueryLensEngine"/>.
///
/// Orchestrates the ALC cache, the Roslyn scripting evaluator, and EF Core
/// model inspection without ever opening a real database connection.
/// </summary>
public sealed partial class QueryLensEngine : IQueryLensEngine, IDbContextPoolProvider
{
    private readonly QueryEvaluator _evaluator;
    private readonly AlcLifecycleManager _alcManager;
    private readonly DbContextPoolManager _poolManager;
    private readonly bool _debugEnabled;
    private readonly ShadowAssemblyCache _shadowCache;
    private bool _disposed;

    public QueryLensEngine(INamespaceTypeIndexCache? namespaceTypeIndexCache = null)
    {
        _debugEnabled = EnvironmentVariableParser.ReadBool("QUERYLENS_DEBUG", fallback: false);
        var poolSize = EnvironmentVariableParser.ReadInt(
            "QUERYLENS_DBCONTEXT_POOL_SIZE",
            fallback: 4,
            min: 1,
            max: 16);
        _evaluator = new QueryEvaluator(namespaceTypeIndexCache);
        _shadowCache = new ShadowAssemblyCache(_debugEnabled);
        _shadowCache.RunStartupCleanup();
        _poolManager = new DbContextPoolManager(poolSize, _debugEnabled);
        _alcManager = new AlcLifecycleManager(
            _shadowCache,
            path => _evaluator.InvalidateMetadataRefCache(path),
            path => _poolManager.EvictForAssemblyAsync(path),
            _debugEnabled);
    }

    // ── IQueryLensEngine ──────────────────────────────────────────────────────

    public async Task<QueryTranslationResult> TranslateAsync(
        TranslationRequest request, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        var fullPath = Path.GetFullPath(request.AssemblyPath);
        var alcCtx = _alcManager.GetOrRefreshContext(fullPath);
        var result = await _evaluator.EvaluateAsync(alcCtx, request, ct, this, fullPath);
        LogTranslationTiming(fullPath, result);

        if (!NeedsDbContextDiscoveryRetry(result))
            return result;

        // Self-heal long-running hosts (LSP): evict the cached context and retry once.
        // This recovers from contexts created before dependency outputs existed or from
        // transient assembly-load failures during initial discovery.
        if (_alcManager.TryRemove(fullPath, out var stale))
            await _alcManager.ReleaseContextAsync(stale, reason: "retry");

        var freshCtx = _alcManager.GetOrRefreshContext(fullPath);
        var retryResult = await _evaluator.EvaluateAsync(freshCtx, request, ct, this, fullPath);
        LogTranslationTiming(fullPath, retryResult);
        return retryResult;
    }

    public Task<ModelSnapshot> InspectModelAsync(
        ModelInspectionRequest request, CancellationToken ct = default)
    {
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(request);

            LogDebug($"inspect-model-start assembly={request.AssemblyPath}");

            try
            {
                var assemblyPath = Path.GetFullPath(request.AssemblyPath);
                var alcCtx = _alcManager.GetOrRefreshContext(assemblyPath);
                Type dbContextType;
                try
                {
                    dbContextType = alcCtx.FindDbContextType(request.DbContextTypeName);
                }
                catch (InvalidOperationException ex) when (QueryEvaluator.IsNoDbContextFoundError(ex))
                {
                    QueryEvaluator.TryLoadSiblingAssemblies(alcCtx);
                    dbContextType = alcCtx.FindDbContextType(request.DbContextTypeName);
                }
                var dbInstance = CreateDbContextForInspection(dbContextType, alcCtx);

                var snapshot = BuildModelSnapshot(dbInstance, dbContextType);
                LogDebug($"inspect-model-success context={snapshot.DbContextType} dbSets={snapshot.DbSetProperties.Count} entities={snapshot.Entities.Count}");
                return Task.FromResult(snapshot);
            }
            catch (Exception ex)
            {
                LogDebug($"inspect-model-failure type={ex.GetType().Name} message={ex.Message}");
                throw;
            }
        }
        catch (Exception exception)
        {
            return Task.FromException<ModelSnapshot>(exception);
        }
    }

    async ValueTask<IDbContextLease> IDbContextPoolProvider.AcquireDbContextLeaseAsync(
        Type dbContextType,
        string assemblyPath,
        IEnumerable<Assembly> userAssemblies,
        CancellationToken cancellationToken)
        => await _poolManager.AcquireLeaseAsync(dbContextType, assemblyPath, userAssemblies, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _alcManager.DisposeAsync();
        await _poolManager.DisposeAsync();
    }

    private void LogTranslationTiming(string assemblyPath, QueryTranslationResult result)
    {
        if (!_debugEnabled)
            return;

        var m = result.Metadata;
        LogDebug(
            "translate-timing " +
            $"assembly={assemblyPath} success={result.Success} " +
            $"totalMs={m.TranslationTime.TotalMilliseconds:F0} " +
            $"contextMs={m.ContextResolutionTime?.TotalMilliseconds:F0} " +
            $"dbContextMs={m.DbContextCreationTime?.TotalMilliseconds:F0} " +
            $"refsMs={m.MetadataReferenceBuildTime?.TotalMilliseconds:F0} " +
            $"compileMs={m.RoslynCompilationTime?.TotalMilliseconds:F0} " +
            $"retries={m.CompilationRetryCount} " +
            $"evalLoadMs={m.EvalAssemblyLoadTime?.TotalMilliseconds:F0} " +
            $"runnerMs={m.RunnerExecutionTime?.TotalMilliseconds:F0}");
    }

    private static bool NeedsDbContextDiscoveryRetry(QueryTranslationResult result) =>
        !result.Success &&
        !string.IsNullOrWhiteSpace(result.ErrorMessage) &&
        result.ErrorMessage.Contains("No DbContext subclass found", StringComparison.OrdinalIgnoreCase);

    private void LogDebug(string message)
    {
        if (!_debugEnabled)
            return;

        Console.Error.WriteLine($"[QL-Engine] {message}");
    }
}