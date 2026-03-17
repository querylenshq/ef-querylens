using System.Collections.Concurrent;
using System.Reflection;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Core.Scripting;
using QueryEvaluator = EFQueryLens.Core.Scripting.Evaluation.QueryEvaluator;

namespace EFQueryLens.Core.Engine;

public sealed partial class QueryLensEngine
{
    private static readonly ConcurrentDictionary<Type, Action<object>?> SClearChangeTrackerCache = new();

    // DbContext pool
    async ValueTask<IDbContextLease> IDbContextPoolProvider.AcquireDbContextLeaseAsync(
        Type dbContextType,
        string assemblyPath,
        IEnumerable<Assembly> userAssemblies,
        CancellationToken cancellationToken)
    {
        var poolKey = BuildDbContextPoolKey(assemblyPath, dbContextType);

        if (!_dbContextPool.TryGetValue(poolKey, out var pool))
        {
            var gateState = _dbContextCreateGates.GetOrAdd(poolKey, static _ => new CreateGateState());
            Interlocked.Increment(ref gateState.ActiveUsers);
            await gateState.Gate.WaitAsync(cancellationToken);
            try
            {
                if (!_dbContextPool.TryGetValue(poolKey, out pool))
                {
                    pool = new PooledDbContextPool(
                        poolKey,
                        dbContextType.FullName!,
                        _dbContextPoolSize);
                    _dbContextPool[poolKey] = pool;

                    if (_debugEnabled)
                    {
                        LogDebug($"dbcontext-pool-create type={dbContextType.Name} size={_dbContextPoolSize}");
                    }
                }
            }
            finally
            {
                gateState.Gate.Release();

                // Once the pooled instance exists and no concurrent creators remain,
                // prune the creation gate entry to avoid long-lived per-key gate objects.
                if (Interlocked.Decrement(ref gateState.ActiveUsers) == 0
                    && _dbContextPool.ContainsKey(poolKey)
                    && _dbContextCreateGates.TryRemove(new KeyValuePair<string, CreateGateState>(poolKey, gateState)))
                {
                    gateState.Gate.Dispose();
                }
            }
        }

        await pool.Gate.WaitAsync(cancellationToken);

        object? leasedInstance;
        var leaseStrategy = "pooled-reuse";

        try
        {
            if (!pool.AvailableInstances.TryDequeue(out leasedInstance))
            {
                lock (pool.CreateLock)
                {
                    if (!pool.AvailableInstances.TryDequeue(out leasedInstance)
                        && pool.CreatedCount < pool.PoolSize)
                    {
                        var (instance, strategy) = QueryEvaluator.CreateDbContextInstance(dbContextType, userAssemblies);
                        leasedInstance = instance;
                        pool.CreatedInstances.Add(instance);
                        pool.CreatedCount++;
                        leaseStrategy = strategy;

                        if (_debugEnabled)
                        {
                            LogDebug(
                                $"dbcontext-pool-instance-create type={dbContextType.Name} " +
                                $"created={pool.CreatedCount}/{pool.PoolSize} strategy={strategy}");
                        }
                    }
                }
            }

            if (leasedInstance is null)
            {
                throw new InvalidOperationException(
                    $"Pool '{pool.PoolKey}' granted a lease slot but no DbContext instance was available.");
            }
        }
        catch
        {
            pool.Gate.Release();
            throw;
        }

        if (_debugEnabled)
        {
            LogDebug($"dbcontext-pool-lease-acquired type={dbContextType.Name} strategy={leaseStrategy}");
        }

        return new DbContextLease(this, pool, leasedInstance, leaseStrategy);
    }

    private void ReleaseDbContextLease(PooledDbContextPool pool, object instance)
    {
        try
        {
            ClearChangeTracker(instance);

            if (_debugEnabled)
            {
                LogDebug($"dbcontext-pool-lease-released type={pool.DbContextTypeFullName}");
            }
        }
        catch (Exception ex)
        {
            LogDebug($"dbcontext-pool-clear-error type={pool.DbContextTypeFullName} error={ex.GetType().Name} message={ex.Message}");
        }
        finally
        {
            pool.AvailableInstances.Enqueue(instance);
            try
            {
                pool.Gate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Pool was disposed/evicted while lease was still in-flight.
            }
        }
    }

    private static void ClearChangeTracker(object dbContextInstance)
    {
        var dbContextType = dbContextInstance.GetType();
        var clearDelegate = SClearChangeTrackerCache.GetOrAdd(dbContextType, static type =>
        {
            var changeTrackerProp = type.GetProperty("ChangeTracker", BindingFlags.Public | BindingFlags.Instance);

            var clearMethod = changeTrackerProp?.PropertyType.GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance);
            if (clearMethod is null)
            {
                return null;
            }

            return instance =>
            {
                var changeTracker = changeTrackerProp?.GetValue(instance);
                if (changeTracker is not null)
                {
                    clearMethod.Invoke(changeTracker, null);
                }
            };
        });

        clearDelegate?.Invoke(dbContextInstance);
    }

    private static string BuildDbContextPoolKey(string assemblyPath, Type dbContextType)
    {
        return $"{Path.GetFullPath(assemblyPath)}|{dbContextType.FullName}";
    }

    private async ValueTask DisposeDbContextPoolAsync()
    {
        if (_debugEnabled && _dbContextPool.Count > 0)
        {
            LogDebug($"dbcontext-pool-dispose count={_dbContextPool.Count}");
        }

        foreach (var pooled in _dbContextPool.Values)
        {
            try
            {
                pooled.Gate.Dispose();

                foreach (var instance in pooled.CreatedInstances)
                {
                    if (instance is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync();
                    }
                    else if (instance is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"dbcontext-pool-dispose-error type={pooled.DbContextTypeFullName} error={ex.GetType().Name} message={ex.Message}");
            }
        }

        _dbContextPool.Clear();

        foreach (var gate in _dbContextCreateGates.Values)
        {
            gate.Gate.Dispose();
        }

        _dbContextCreateGates.Clear();
    }

    private async ValueTask EvictPooledDbContextsForAssemblyAsync(string sourceAssemblyPath)
    {
        var fullPath = Path.GetFullPath(sourceAssemblyPath);
        var prefix = fullPath + "|";
        var keys = _dbContextPool.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();

        foreach (var key in keys)
        {
            if (_dbContextPool.TryRemove(key, out var pooled))
            {
                try
                {
                    pooled.Gate.Dispose();

                    foreach (var instance in pooled.CreatedInstances)
                    {
                        if (instance is IAsyncDisposable asyncDisposable)
                        {
                            await asyncDisposable.DisposeAsync();
                        }
                        else if (instance is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"dbcontext-pool-evict-dispose-error key={key} error={ex.GetType().Name} message={ex.Message}");
                }
            }

            if (_dbContextCreateGates.TryRemove(key, out var createGate))
            {
                createGate.Gate.Dispose();
            }
        }

        if (_debugEnabled && keys.Length > 0)
        {
            LogDebug($"dbcontext-pool-evict assembly={fullPath} removed={keys.Length}");
        }
    }
}
