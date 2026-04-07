using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using EFQueryLens.Core.AssemblyContext;

namespace EFQueryLens.Core.Engine;

internal sealed class AlcLifecycleManager
{
    internal sealed record CachedAssemblyContext(
        string SourceAssemblyPath,
        string SourceFingerprint,
        string ShadowAssemblyPath,
        ProjectAssemblyContext Context);

    private readonly ConcurrentDictionary<string, CachedAssemblyContext> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, object> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ShadowAssemblyCache _shadowCache;
    private readonly Action<string> _invalidateEvalCache;
    private readonly Func<string, ValueTask> _evictPool;
    private readonly bool _debugEnabled;

    internal AlcLifecycleManager(
        ShadowAssemblyCache shadowCache,
        Action<string> invalidateEvalCache,
        Func<string, ValueTask> evictPool,
        bool debugEnabled)
    {
        _shadowCache = shadowCache;
        _invalidateEvalCache = invalidateEvalCache;
        _evictPool = evictPool;
        _debugEnabled = debugEnabled;
    }

    internal ProjectAssemblyContext GetOrRefreshContext(string assemblyPath)
    {
        var sourceAssemblyPath = Path.GetFullPath(assemblyPath);
        var gate = _gates.GetOrAdd(sourceAssemblyPath, static _ => new object());
        lock (gate)
        {
            var sourceFingerprint = BuildSourceFingerprint(sourceAssemblyPath);
            var shadowAssemblyPath = _shadowCache.ResolveOrCreateBundle(sourceAssemblyPath);

            if (_cache.TryGetValue(sourceAssemblyPath, out var existing))
            {
                if (string.Equals(existing.SourceFingerprint, sourceFingerprint, StringComparison.Ordinal)
                    && string.Equals(existing.ShadowAssemblyPath, shadowAssemblyPath, StringComparison.OrdinalIgnoreCase)
                    && !ProjectAssemblyContextFactory.IsStale(existing.Context))
                {
                    return existing.Context;
                }

                _cache.TryRemove(sourceAssemblyPath, out _);
                QueueReleaseContext(existing, reason: "stale");
            }

            var freshContext = ProjectAssemblyContextFactory.Create(shadowAssemblyPath);
            var cachedContext = new CachedAssemblyContext(
                sourceAssemblyPath,
                sourceFingerprint,
                shadowAssemblyPath,
                freshContext);

            _cache[sourceAssemblyPath] = cachedContext;
            return freshContext;
        }
    }

    internal bool TryRemove(string fullPath, [NotNullWhen(true)] out CachedAssemblyContext? entry) =>
        _cache.TryRemove(fullPath, out entry);

    internal void QueueReleaseContext(CachedAssemblyContext entry, string reason)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ReleaseContextAsync(entry, reason);
            }
            catch (Exception ex)
            {
                LogDebug($"alc-release-failure reason={reason} source={entry.SourceAssemblyPath} error={ex.GetType().Name} message={ex.Message}");
            }
        });
    }

    internal async ValueTask ReleaseContextAsync(CachedAssemblyContext entry, string reason)
    {
        _invalidateEvalCache(entry.ShadowAssemblyPath);
        await _evictPool(entry.SourceAssemblyPath);
        entry.Context.Dispose();
        ForceUnloadCollection(aggressive: string.Equals(reason, "dispose", StringComparison.Ordinal));

        if (!_cache.ContainsKey(entry.SourceAssemblyPath))
            _gates.TryRemove(entry.SourceAssemblyPath, out _);

        LogDebug($"alc-release reason={reason} source={entry.SourceAssemblyPath} shadow={entry.ShadowAssemblyPath}");
    }

    internal async ValueTask DisposeAsync()
    {
        foreach (var entry in _cache.Values)
            await ReleaseContextAsync(entry, "dispose");
        _cache.Clear();
        _gates.Clear();
    }

    private static string BuildSourceFingerprint(string sourceAssemblyPath)
    {
        var info = new FileInfo(sourceAssemblyPath);
        return $"{Path.GetFullPath(sourceAssemblyPath)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }

    private static void ForceUnloadCollection(bool aggressive)
    {
        if (!aggressive)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false, compacting: false);
            return;
        }

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
    }

    private void LogDebug(string message)
    {
        if (!_debugEnabled)
            return;
        Console.Error.WriteLine($"[QL-Engine] {message}");
    }
}
