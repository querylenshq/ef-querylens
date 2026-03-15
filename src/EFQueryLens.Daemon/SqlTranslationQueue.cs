using System.Collections.Concurrent;
using System.Threading.Channels;
using EFQueryLens.Core;
using Microsoft.Extensions.Hosting;

namespace EFQueryLens.Daemon;

internal sealed partial class SqlTranslationQueue : BackgroundService
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

}
