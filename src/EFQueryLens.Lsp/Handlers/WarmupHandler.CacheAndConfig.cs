namespace EFQueryLens.Lsp.Handlers;

internal sealed partial class WarmupHandler
{
    private bool TryGetCachedWarmup(string assemblyPath, out CachedWarmup cached)
    {
        cached = default!;
        if (!_warmCache.TryGetValue(assemblyPath, out var existing))
        {
            return false;
        }

        if (existing.ExpiresAtUtcTicks <= DateTime.UtcNow.Ticks)
        {
            _warmCache.TryRemove(assemblyPath, out _);
            return false;
        }

        cached = existing;
        return true;
    }

    private void CacheWarmup(string assemblyPath, bool success, string message)
    {
        var ttlMs = success ? _successTtlMs : _failureCooldownMs;
        if (ttlMs <= 0)
        {
            _warmCache.TryRemove(assemblyPath, out _);
            return;
        }

        var expires = DateTime.UtcNow.AddMilliseconds(ttlMs).Ticks;
        _warmCache[assemblyPath] = new CachedWarmup(expires, success, message);
    }

    private void LogDebug(string message)
    {
        if (!_debugEnabled)
        {
            return;
        }

        Console.Error.WriteLine($"[QL-Warmup] {message}");
    }
}
