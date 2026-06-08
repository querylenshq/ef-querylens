using EFQueryLens.Core;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Daemon;
using Microsoft.Extensions.Caching.Memory;

namespace EFQueryLens.Core.Tests.Daemon;

/// <summary>
/// Covers the daemon translation coordinator: per-translation timeout, negative-result caching,
/// success caching, and interactive-vs-warm concurrency fairness.
/// </summary>
public sealed class TranslationCoordinatorTests
{
    private static TranslationRequest Request(string expression) =>
        new() { Expression = expression, AssemblyPath = string.Empty };

    private static QueryTranslationResult Ok(string sql = "SELECT 1") => new()
    {
        Success = true,
        Sql = sql,
        Metadata = new TranslationMetadata { DbContextType = "Db", EfCoreVersion = "9", ProviderName = "sqlite" },
    };

    private static QueryTranslationResult Fail(string message = "boom") => new()
    {
        Success = false,
        ErrorMessage = message,
        Metadata = new TranslationMetadata { DbContextType = "", EfCoreVersion = "", ProviderName = "" },
    };

    private static TranslationCoordinator Create(FakeEngine engine, TranslationCoordinatorOptions options) =>
        new(engine, new MemoryCache(new MemoryCacheOptions()), store: null, options);

    private static TranslationCoordinatorOptions Options(
        int timeoutMs = 30_000,
        int negativeTtlMs = 5_000,
        int maxConcurrent = 4,
        int maxWarm = 2)
        => new(timeoutMs, negativeTtlMs, maxConcurrent, maxWarm);

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task TranslateAsync_EngineExceedsTimeout_ReturnsTimeoutFailure()
    {
        var engine = new FakeEngine((_, ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => Ok()));
        using var coordinator = Create(engine, Options(timeoutMs: 100));

        var result = await coordinator.TranslateAsync(Request("db.Slow"));

        Assert.False(result.Success);
        Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(engine.Calls >= 1);
    }

    // ── Negative caching ────────────────────────────────────────────────────────

    [Fact]
    public async Task TranslateAsync_FailureWithNegativeCache_NotRecomputed()
    {
        var engine = new FakeEngine((_, _) => Task.FromResult(Fail()));
        using var coordinator = Create(engine, Options(negativeTtlMs: 5_000));

        await coordinator.TranslateAsync(Request("db.Bad"));
        await coordinator.TranslateAsync(Request("db.Bad"));

        Assert.Equal(1, engine.Calls); // second call served from the negative cache
    }

    [Fact]
    public async Task TranslateAsync_FailureWithNegativeCacheDisabled_Recomputed()
    {
        var engine = new FakeEngine((_, _) => Task.FromResult(Fail()));
        using var coordinator = Create(engine, Options(negativeTtlMs: 0));

        await coordinator.TranslateAsync(Request("db.Bad"));
        await coordinator.TranslateAsync(Request("db.Bad"));

        Assert.Equal(2, engine.Calls); // failures not cached → recomputed
    }

    [Fact]
    public async Task TranslateAsync_Success_IsCached()
    {
        var engine = new FakeEngine((_, _) => Task.FromResult(Ok()));
        using var coordinator = Create(engine, Options());

        var first = await coordinator.TranslateAsync(Request("db.Good"));
        var second = await coordinator.TranslateAsync(Request("db.Good"));

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(1, engine.Calls);
    }

    // ── Concurrency fairness ──────────────────────────────────────────────────

    [Fact]
    public async Task Warm_ConcurrencyNeverExceedsWarmCap()
    {
        var gate = new TaskCompletionSource<QueryTranslationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new FakeEngine((_, _) => gate.Task);
        using var coordinator = Create(engine, Options(maxConcurrent: 4, maxWarm: 2));

        // Distinct expressions → distinct cache keys → no dedup; all five contend for the warm gate.
        var warms = Enumerable.Range(0, 5).Select(i => coordinator.Warm(Request($"db.Warm{i}"))).ToArray();

        await WaitUntilAsync(() => engine.Current >= 2, TimeSpan.FromSeconds(5));
        await Task.Delay(100); // give any (incorrectly) un-gated extra warms a chance to slip in

        Assert.True(engine.Peak <= 2, $"warm peak concurrency {engine.Peak} exceeded cap 2");

        gate.SetResult(Ok());
        await Task.WhenAll(warms);
        Assert.Equal(5, engine.Calls);
        Assert.True(engine.Peak <= 2);
    }

    [Fact]
    public async Task Interactive_ProceedsWhileWarmGateSaturated()
    {
        var warmGate = new TaskCompletionSource<QueryTranslationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new FakeEngine((req, _) =>
            req.Expression.StartsWith("db.Warm", StringComparison.Ordinal)
                ? warmGate.Task                       // warm requests block
                : Task.FromResult(Ok()));             // interactive returns immediately

        using var coordinator = Create(engine, Options(maxConcurrent: 4, maxWarm: 2));

        // Saturate the warm gate: two blocked warms occupying two engine slots.
        var warms = new[] { coordinator.Warm(Request("db.Warm0")), coordinator.Warm(Request("db.Warm1")) };
        await WaitUntilAsync(() => engine.Current >= 2, TimeSpan.FromSeconds(5));

        // Interactive translate must still complete — it skips the warm gate and has free engine slots.
        var interactive = coordinator.TranslateAsync(Request("db.Interactive"));
        var done = await Task.WhenAny(interactive, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(done == interactive, "interactive translate was blocked behind the warm gate");
        Assert.True((await interactive).Success);

        warmGate.SetResult(Ok());
        await Task.WhenAll(warms);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("Condition not met within the allotted time.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class FakeEngine : IQueryLensEngine
    {
        private readonly Func<TranslationRequest, CancellationToken, Task<QueryTranslationResult>> _behavior;
        private int _calls;
        private int _current;
        private int _peak;

        public FakeEngine(Func<TranslationRequest, CancellationToken, Task<QueryTranslationResult>> behavior)
            => _behavior = behavior;

        public int Calls => Volatile.Read(ref _calls);
        public int Current => Volatile.Read(ref _current);
        public int Peak => Volatile.Read(ref _peak);

        public async Task<QueryTranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            var current = Interlocked.Increment(ref _current);
            UpdatePeak(current);
            try
            {
                return await _behavior(request, ct);
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }

        public Task<ModelSnapshot> InspectModelAsync(ModelInspectionRequest request, CancellationToken ct = default)
            => Task.FromResult(new ModelSnapshot { DbContextType = string.Empty });

        public Task InvalidateAssemblyCachesAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void UpdatePeak(int candidate)
        {
            int observed;
            while (candidate > (observed = Volatile.Read(ref _peak)))
            {
                if (Interlocked.CompareExchange(ref _peak, candidate, observed) == observed)
                {
                    return;
                }
            }
        }
    }
}
