using EFQueryLens.Core;

namespace EFQueryLens.Daemon;

internal sealed partial class SqlTranslationQueue
{
    public CacheInvalidationSummary InvalidateQueryCaches()
    {
        Interlocked.Increment(ref _cacheEpoch);

        var removedCachedResults = _resultCache.Count;
        var removedInflightJobs = _inflightJobs.Count;

        _resultCache.Clear();
        _inflightJobs.Clear();

        LogDebug($"queue-cache-invalidate cachedRemoved={removedCachedResults} inflightRemoved={removedInflightJobs}");

        return new CacheInvalidationSummary(removedCachedResults, removedInflightJobs);
    }

    private bool TryGetCachedReady(string semanticKey, out CachedTranslation? cached)
    {
        cached = null;
        if (_cacheTtlMs <= 0)
        {
            return false;
        }

        if (!_resultCache.TryGetValue(semanticKey, out var found))
        {
            return false;
        }

        var expiresAtTicks = found.CreatedAtTicks + TimeSpan.FromMilliseconds(_cacheTtlMs).Ticks;
        if (expiresAtTicks <= DateTime.UtcNow.Ticks)
        {
            _resultCache.TryRemove(semanticKey, out _);
            return false;
        }

        cached = found;
        return true;
    }

    private void SweepExpiredCacheIfNeeded()
    {
        if (_cacheTtlMs <= 0 || _resultCache.IsEmpty)
        {
            return;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        var lastSweepTicks = Interlocked.Read(ref _lastSweepTicks);
        var sweepIntervalTicks = TimeSpan.FromSeconds(5).Ticks;
        if (nowTicks - lastSweepTicks < sweepIntervalTicks)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastSweepTicks, nowTicks, lastSweepTicks) != lastSweepTicks)
        {
            return;
        }

        var ttlTicks = TimeSpan.FromMilliseconds(_cacheTtlMs).Ticks;
        foreach (var entry in _resultCache)
        {
            if (entry.Value.CreatedAtTicks + ttlTicks > nowTicks)
            {
                continue;
            }

            _resultCache.TryRemove(entry.Key, out _);
        }
    }

    private static QueryTranslationResult BuildFailureResult(string message)
    {
        return new QueryTranslationResult
        {
            Success = false,
            ErrorMessage = message,
            Metadata = new TranslationMetadata
            {
                DbContextType = string.Empty,
                EfCoreVersion = string.Empty,
                ProviderName = string.Empty,
                CreationStrategy = "daemon-queue-error",
            },
        };
    }

    private static int ReadIntEnvironmentVariable(string variableName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);
        if (!int.TryParse(raw, out var value))
        {
            return fallback;
        }

        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static bool ReadBoolEnvironmentVariable(string variableName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private void LogDebug(string message)
    {
        if (!_debugEnabled)
        {
            return;
        }

        Console.Error.WriteLine($"[QL-DAEMON-QUEUE] {message}");
    }

    public readonly record struct CacheInvalidationSummary(int RemovedCachedResults, int RemovedInflightJobs);
}
