using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using EFQueryLens.Core;
using Microsoft.Extensions.Hosting;

namespace EFQueryLens.Daemon;

internal sealed class SqlTranslationQueue : BackgroundService
{
    private sealed record TranslationWorkItem(
        string SemanticKey,
        string ContextName,
        TranslationRequest Request,
        string JobId,
        long Epoch);

    private sealed record CachedTranslation(
        long CreatedAtTicks,
        string JobId,
        QueryTranslationResult Result);

    private readonly Channel<TranslationWorkItem> _channel;
    private readonly ConcurrentDictionary<string, string> _inflightJobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CachedTranslation> _resultCache = new(StringComparer.Ordinal);
    private readonly IQueryLensEngine _engine;
    private readonly TranslationMetrics _metrics;
    private readonly bool _debugEnabled;
    private readonly int _cacheTtlMs;
    private long _lastSweepTicks;
    private long _cacheEpoch;

    public SqlTranslationQueue(IQueryLensEngine engine, TranslationMetrics metrics)
    {
        _engine = engine;
        _metrics = metrics;
        _debugEnabled = ReadBoolEnvironmentVariable("QUERYLENS_DEBUG", fallback: false);
        _cacheTtlMs = ReadIntEnvironmentVariable(
            "QUERYLENS_HOVER_CACHE_TTL_MS",
            fallback: 15_000,
            min: 0,
            max: 120_000);

        var queueCapacity = ReadIntEnvironmentVariable(
            "QUERYLENS_TRANSLATION_QUEUE_CAPACITY",
            fallback: 50,
            min: 10,
            max: 2_000);

        _channel = Channel.CreateBounded<TranslationWorkItem>(new BoundedChannelOptions(queueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public async Task<QueuedTranslationResult> EnqueueOrGetAsync(
        string semanticKey,
        string contextName,
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticKey);
        ArgumentNullException.ThrowIfNull(request);

        SweepExpiredCacheIfNeeded();

        if (TryGetCachedReady(semanticKey, out var cached))
        {
            LogDebug($"queue-cache-hit context={contextName} semanticKeyLen={semanticKey.Length}");
            return new QueuedTranslationResult
            {
                Status = QueryTranslationStatus.Ready,
                JobId = cached!.JobId,
                AverageTranslationMs = _metrics.GetAverageMs(contextName),
                Result = cached.Result,
            };
        }

        if (_inflightJobs.TryGetValue(semanticKey, out var existingJobId))
        {
            var existingStatus = _metrics.IsWarming(contextName)
                ? QueryTranslationStatus.Starting
                : QueryTranslationStatus.InQueue;
            LogDebug($"queue-inflight-hit context={contextName} semanticKeyLen={semanticKey.Length} status={existingStatus}");
            return new QueuedTranslationResult
            {
                Status = existingStatus,
                JobId = existingJobId,
                AverageTranslationMs = _metrics.GetAverageMs(contextName),
            };
        }

        var jobId = Guid.NewGuid().ToString("N");
        if (!_inflightJobs.TryAdd(semanticKey, jobId))
        {
            var raceStatus = _metrics.IsWarming(contextName)
                ? QueryTranslationStatus.Starting
                : QueryTranslationStatus.InQueue;
            return new QueuedTranslationResult
            {
                Status = raceStatus,
                JobId = jobId,
                AverageTranslationMs = _metrics.GetAverageMs(contextName),
            };
        }

        try
        {
            var workItem = new TranslationWorkItem(
                semanticKey,
                contextName,
                request,
                jobId,
                Interlocked.Read(ref _cacheEpoch));
            await _channel.Writer.WriteAsync(workItem, cancellationToken);

            var status = _metrics.IsWarming(contextName)
                ? QueryTranslationStatus.Starting
                : QueryTranslationStatus.InQueue;
            LogDebug($"queue-enqueue context={contextName} semanticKeyLen={semanticKey.Length} status={status}");

            return new QueuedTranslationResult
            {
                Status = status,
                JobId = jobId,
                AverageTranslationMs = _metrics.GetAverageMs(contextName),
            };
        }
        catch
        {
            _inflightJobs.TryRemove(semanticKey, out _);
            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            var sw = Stopwatch.StartNew();
            QueryTranslationResult result;

            try
            {
                result = await _engine.TranslateAsync(workItem.Request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                result = BuildFailureResult($"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                sw.Stop();
            }

            _metrics.RecordSample(workItem.ContextName, sw.ElapsedMilliseconds);

            var currentEpoch = Interlocked.Read(ref _cacheEpoch);
            if (workItem.Epoch == currentEpoch)
            {
                _resultCache[workItem.SemanticKey] = new CachedTranslation(
                    DateTime.UtcNow.Ticks,
                    workItem.JobId,
                    result);
            }

            _inflightJobs.TryRemove(workItem.SemanticKey, out _);

            LogDebug(
                $"queue-complete context={workItem.ContextName} semanticKeyLen={workItem.SemanticKey.Length} " +
                $"elapsedMs={sw.ElapsedMilliseconds} success={result.Success}");
        }
    }

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
