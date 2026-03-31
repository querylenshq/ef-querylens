using System.Collections.Concurrent;
using System.Reflection;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Core.Scripting;
using QueryEvaluator = EFQueryLens.Core.Scripting.Evaluation.QueryEvaluator;

namespace EFQueryLens.Core.Engine;

internal sealed class DbContextPoolManager
{
    private sealed class PooledDbContextPool(
        string poolKey,
        string dbContextTypeFullName,
        int poolSize)
    {
        public string PoolKey { get; } = poolKey;
        public string DbContextTypeFullName { get; } = dbContextTypeFullName;
        public int PoolSize { get; } = poolSize;
        public SemaphoreSlim Gate { get; } = new(poolSize, poolSize);
        public object CreateLock { get; } = new();
        public ConcurrentQueue<object> AvailableInstances { get; } = new();
        public ConcurrentBag<object> CreatedInstances { get; } = [];
        public int CreatedCount;
    }

    private sealed class CreateGateState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int ActiveUsers;
    }

    private sealed class DbContextLease(
        DbContextPoolManager owner,
        PooledDbContextPool pool,
        object instance,
        string strategy)
        : IDbContextLease
    {
        private bool _disposed;

        public object Instance => instance;
        public string PoolKey => pool.PoolKey;
        public string Strategy { get; } = strategy;

        public ValueTask DisposeAsync()
        {
            if (_disposed)
                return ValueTask.CompletedTask;

            _disposed = true;
            owner.ReleaseLease(pool, instance);
            return ValueTask.CompletedTask;
        }
    }

    private static readonly ConcurrentDictionary<Type, Action<object>?> SClearChangeTrackerCache = new();
    private readonly ConcurrentDictionary<string, PooledDbContextPool> _pool = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CreateGateState> _createGates = new(StringComparer.Ordinal);
    private readonly int _poolSize;
    private readonly bool _debugEnabled;

    internal DbContextPoolManager(int poolSize, bool debugEnabled)
    {
        _poolSize = poolSize;
        _debugEnabled = debugEnabled;
    }

    internal async ValueTask<IDbContextLease> AcquireLeaseAsync(
        Type dbContextType,
        string assemblyPath,
        IEnumerable<Assembly> userAssemblies,
        CancellationToken cancellationToken)
    {
        var poolKey = BuildPoolKey(assemblyPath, dbContextType);

        while (true)
        {
            if (!_pool.TryGetValue(poolKey, out var pool))
            {
                var gateState = _createGates.GetOrAdd(poolKey, static _ => new CreateGateState());
                Interlocked.Increment(ref gateState.ActiveUsers);
                await gateState.Gate.WaitAsync(cancellationToken);
                try
                {
                    if (!_pool.TryGetValue(poolKey, out pool))
                    {
                        pool = new PooledDbContextPool(poolKey, dbContextType.FullName!, _poolSize);
                        _pool[poolKey] = pool;

                        if (_debugEnabled)
                            LogDebug($"dbcontext-pool-create type={dbContextType.Name} size={_poolSize}");
                    }
                }
                finally
                {
                    gateState.Gate.Release();

                    if (Interlocked.Decrement(ref gateState.ActiveUsers) == 0
                        && _pool.ContainsKey(poolKey)
                        && _createGates.TryRemove(new KeyValuePair<string, CreateGateState>(poolKey, gateState)))
                    {
                        gateState.Gate.Dispose();
                    }
                }
            }

            try
            {
                await pool.Gate.WaitAsync(cancellationToken);
            }
            catch (ObjectDisposedException)
            {
                _pool.TryRemove(poolKey, out _);

                if (_debugEnabled)
                    LogDebug($"dbcontext-pool-lease-race-evicted type={dbContextType.Name} key={poolKey}");

                continue;
            }

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
                try
                {
                    pool.Gate.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Pool was disposed/evicted while acquire path was unwinding.
                }

                throw;
            }

            if (_debugEnabled)
                LogDebug($"dbcontext-pool-lease-acquired type={dbContextType.Name} strategy={leaseStrategy}");

            return new DbContextLease(this, pool, leasedInstance, leaseStrategy);
        }
    }

    private void ReleaseLease(PooledDbContextPool pool, object instance)
    {
        try
        {
            ClearChangeTracker(instance);

            if (_debugEnabled)
                LogDebug($"dbcontext-pool-lease-released type={pool.DbContextTypeFullName}");
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

    internal async ValueTask EvictForAssemblyAsync(string sourceAssemblyPath)
    {
        var fullPath = Path.GetFullPath(sourceAssemblyPath);
        var prefix = fullPath + "|";
        var keys = _pool.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();

        foreach (var key in keys)
        {
            if (_pool.TryRemove(key, out var pooled))
            {
                try
                {
                    pooled.Gate.Dispose();

                    foreach (var instance in pooled.CreatedInstances)
                    {
                        if (instance is IAsyncDisposable asyncDisposable)
                            await asyncDisposable.DisposeAsync();
                        else if (instance is IDisposable disposable)
                            disposable.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"dbcontext-pool-evict-dispose-error key={key} error={ex.GetType().Name} message={ex.Message}");
                }
            }

            if (_createGates.TryRemove(key, out var createGate))
                createGate.Gate.Dispose();
        }

        if (_debugEnabled && keys.Length > 0)
            LogDebug($"dbcontext-pool-evict assembly={fullPath} removed={keys.Length}");
    }

    internal async ValueTask DisposeAsync()
    {
        if (_debugEnabled && _pool.Count > 0)
            LogDebug($"dbcontext-pool-dispose count={_pool.Count}");

        foreach (var pooled in _pool.Values)
        {
            try
            {
                pooled.Gate.Dispose();

                foreach (var instance in pooled.CreatedInstances)
                {
                    if (instance is IAsyncDisposable asyncDisposable)
                        await asyncDisposable.DisposeAsync();
                    else if (instance is IDisposable disposable)
                        disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogDebug($"dbcontext-pool-dispose-error type={pooled.DbContextTypeFullName} error={ex.GetType().Name} message={ex.Message}");
            }
        }

        _pool.Clear();

        foreach (var gate in _createGates.Values)
            gate.Gate.Dispose();

        _createGates.Clear();
    }

    private static void ClearChangeTracker(object dbContextInstance)
    {
        var dbContextType = dbContextInstance.GetType();
        var clearDelegate = SClearChangeTrackerCache.GetOrAdd(dbContextType, static type =>
        {
            var changeTrackerProp = type.GetProperty("ChangeTracker", BindingFlags.Public | BindingFlags.Instance);
            var clearMethod = changeTrackerProp?.PropertyType.GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance);
            if (clearMethod is null)
                return null;

            return instance =>
            {
                var changeTracker = changeTrackerProp?.GetValue(instance);
                if (changeTracker is not null)
                    clearMethod.Invoke(changeTracker, null);
            };
        });

        clearDelegate?.Invoke(dbContextInstance);
    }

    private static string BuildPoolKey(string assemblyPath, Type dbContextType) =>
        $"{Path.GetFullPath(assemblyPath)}|{dbContextType.FullName}";

    private void LogDebug(string message)
    {
        if (!_debugEnabled)
            return;
        Console.Error.WriteLine($"[QL-Engine] {message}");
    }
}
