using EFQueryLens.Core;
using EFQueryLens.Core.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace EFQueryLens.Daemon;

/// <summary>
/// Tunables for translation generation. Resolved from environment variables with sane,
/// clamped defaults.
/// </summary>
internal sealed record TranslationCoordinatorOptions(
    int TimeoutMs,
    int NegativeCacheTtlMs,
    int MaxConcurrent,
    int MaxWarmConcurrent)
{
    public static TranslationCoordinatorOptions FromEnvironment()
    {
        var maxConcurrent = ReadInt("QUERYLENS_MAX_CONCURRENT_TRANSLATIONS", Math.Max(2, Environment.ProcessorCount / 2), 1, 64);

        // Warm work is always capped below the engine limit so an interactive /translate keeps at
        // least one free engine slot and is never stuck behind a file-open warm storm.
        var warmCeiling = Math.Max(1, maxConcurrent - 1);
        var maxWarm = Math.Min(ReadInt("QUERYLENS_MAX_CONCURRENT_WARM", warmCeiling, 1, 64), warmCeiling);

        return new TranslationCoordinatorOptions(
            TimeoutMs: ReadInt("QUERYLENS_TRANSLATE_TIMEOUT_MS", 15_000, 1_000, 120_000),
            NegativeCacheTtlMs: ReadInt("QUERYLENS_NEGATIVE_CACHE_TTL_MS", 5_000, 0, 60_000),
            MaxConcurrent: maxConcurrent,
            MaxWarmConcurrent: maxWarm);
    }

    private static int ReadInt(string name, int fallback, int min, int max)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? Math.Clamp(v, min, max) : fallback;
}

/// <summary>
/// Owns translation request handling for the daemon: the in-memory + SQLite caches, in-flight
/// deduplication, a hard per-translation timeout, negative-result caching, and concurrency gates
/// that keep interactive hovers ahead of background warm sweeps.
///
/// Extracted from <c>Program</c> so this logic is unit-testable with a fake engine.
/// </summary>
internal sealed class TranslationCoordinator : IDisposable
{
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromSeconds(60);

    private readonly IQueryLensEngine _engine;
    private readonly IMemoryCache _cache;
    private readonly QueryResultStore? _store;
    private readonly TranslationCoordinatorOptions _options;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<QueryTranslationResult>>> _inflight
        = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _engineGate;
    private readonly SemaphoreSlim _warmGate;

    public TranslationCoordinator(
        IQueryLensEngine engine,
        IMemoryCache cache,
        QueryResultStore? store,
        TranslationCoordinatorOptions options)
    {
        _engine = engine;
        _cache = cache;
        _store = store;
        _options = options;
        _engineGate = new SemaphoreSlim(options.MaxConcurrent, options.MaxConcurrent);
        _warmGate = new SemaphoreSlim(options.MaxWarmConcurrent, options.MaxWarmConcurrent);
    }

    /// <summary>
    /// Interactive translation (hover). Returns a cached/persisted result instantly, otherwise
    /// computes under the engine gate with a timeout and caches the result.
    /// </summary>
    public async Task<QueryTranslationResult> TranslateAsync(TranslationRequest request)
    {
        var key = TranslationCacheKey.Compute(request);

        if (TryGetCached(key, out var cached))
        {
            return cached!;
        }

        var lazy = _inflight.GetOrAdd(
            key,
            _ => new Lazy<Task<QueryTranslationResult>>(
                () => RunGuardedAsync(request),
                LazyThreadSafetyMode.ExecutionAndPublication));

        QueryTranslationResult result;
        try
        {
            result = await lazy.Value;
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }

        CacheResult(key, request, result);
        return result;
    }

    /// <summary>
    /// Fire-and-forget warm. Schedules a background translation (bounded by the warm gate so it
    /// can't starve interactive work) unless the result is already cached/persisted. Returns the
    /// scheduled task purely so tests can await completion; production ignores it.
    /// </summary>
    public Task Warm(TranslationRequest request)
    {
        var key = TranslationCacheKey.Compute(request);

        if (TryGetCached(key, out _))
        {
            return Task.CompletedTask;
        }

        return _inflight.GetOrAdd(
            key,
            k => new Lazy<Task<QueryTranslationResult>>(
                () => Task.Run(async () =>
                {
                    await _warmGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        var r = await RunGuardedAsync(request).ConfigureAwait(false);
                        CacheResult(k, request, r);
                        return r;
                    }
                    finally
                    {
                        _warmGate.Release();
                        _inflight.TryRemove(k, out _);
                    }
                }),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    /// <summary>Clears the in-memory hot cache and the durable SQLite store.</summary>
    public void Invalidate()
    {
        if (_cache is MemoryCache mc)
        {
            mc.Clear();
        }

        _store?.Clear();
    }

    private bool TryGetCached(string key, out QueryTranslationResult? result)
    {
        if (_cache.TryGetValue<QueryTranslationResult>(key, out var cached) && cached is not null)
        {
            result = cached;
            return true;
        }

        var persisted = _store?.TryGet(key);
        if (persisted is not null)
        {
            _cache.Set(key, persisted, SuccessTtl);
            result = persisted;
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>
    /// Runs one engine translation under the global concurrency gate with a hard timeout.
    /// The returned task completes promptly on timeout; the engine slot is released only when the
    /// underlying work actually finishes — so a query that ignores cancellation can't hang the
    /// request, but also can't return its slot to the pool until it truly ends.
    /// </summary>
    private async Task<QueryTranslationResult> RunGuardedAsync(TranslationRequest request)
    {
        await _engineGate.WaitAsync().ConfigureAwait(false);

        var cts = new CancellationTokenSource();
        Task<QueryTranslationResult> work;
        try
        {
            work = _engine.TranslateAsync(request, cts.Token);
        }
        catch (Exception ex)
        {
            _engineGate.Release();
            cts.Dispose();
            return Failure(ex.Message);
        }

        // Release the engine slot when the real work completes, regardless of timeout.
        _ = work.ContinueWith(
            t =>
            {
                _engineGate.Release();
                cts.Dispose();
                _ = t.Exception; // observe to avoid unobserved-task-exception noise
            },
            TaskScheduler.Default);

        using var timeoutCts = new CancellationTokenSource();
        var delay = Task.Delay(_options.TimeoutMs, timeoutCts.Token);
        var finished = await Task.WhenAny(work, delay).ConfigureAwait(false);

        if (finished != work)
        {
            cts.Cancel(); // best-effort signal if the engine honors cancellation
            Console.Error.WriteLine($"[QL-Engine] translate-timeout ms={_options.TimeoutMs}");
            return Failure("Translation timed out.");
        }

        timeoutCts.Cancel(); // stop the delay timer; work won the race
        try
        {
            return await work.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failure("Translation timed out.");
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    private void CacheResult(string key, TranslationRequest request, QueryTranslationResult result)
    {
        if (result.Success)
        {
            _cache.Set(key, result, SuccessTtl);
            _store?.Set(
                key,
                request.AssemblyPath ?? string.Empty,
                TranslationCacheKey.ComputeAssemblyFingerprint(request.AssemblyPath),
                result);
        }
        else if (_options.NegativeCacheTtlMs > 0)
        {
            // Short-lived negative cache: stop recompiling a query that currently fails on every
            // hover/warm. Never persisted to SQLite; the assembly fingerprint is already in the
            // key (so a rebuild busts it) and the short TTL lets a transient failure recover.
            _cache.Set(key, result, TimeSpan.FromMilliseconds(_options.NegativeCacheTtlMs));
        }
    }

    private static QueryTranslationResult Failure(string message) => new()
    {
        Success = false,
        ErrorMessage = message,
        Metadata = new TranslationMetadata
        {
            DbContextType = string.Empty,
            EfCoreVersion = string.Empty,
            ProviderName = string.Empty,
        },
    };

    public void Dispose()
    {
        _engineGate.Dispose();
        _warmGate.Dispose();
    }
}
