using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp.HoverPipeline;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class HoverResultCacheTests
{
    private const string Fingerprint = "fp|1|2";
    private const string SemanticKey = "sem-key";

    [Fact]
    public void TryStoreInQueue_FirstCall_ReturnsTrue()
    {
        var cache = CreateCache();
        Assert.True(cache.TryStoreInQueue(Fingerprint, SemanticKey));
    }

    [Fact]
    public void TryStoreInQueue_SecondCallSameKey_ReturnsFalse()
    {
        var cache = CreateCache();
        cache.TryStoreInQueue(Fingerprint, SemanticKey);
        Assert.False(cache.TryStoreInQueue(Fingerprint, SemanticKey));
    }

    [Fact]
    public void TryStoreInQueue_DifferentKeys_BothReturnTrue()
    {
        var cache = CreateCache();
        Assert.True(cache.TryStoreInQueue(Fingerprint, "key-a"));
        Assert.True(cache.TryStoreInQueue(Fingerprint, "key-b"));
    }

    [Fact]
    public void TryGetAny_InQueueEntry_IsRetrievable()
    {
        var cache = CreateCache();
        cache.TryStoreInQueue(Fingerprint, SemanticKey);

        Assert.True(cache.TryGetAny(Fingerprint, SemanticKey, out var result));
        Assert.Equal(QueryTranslationStatus.InQueue, result!.Status);
    }

    [Fact]
    public void TryStoreInQueue_WhenCacheDisabled_ReturnsFalse()
    {
        var cache = new HoverResultCache(hoverCacheTtlMs: 0, inQueueCacheTtlMs: 3_000);
        Assert.False(cache.TryStoreInQueue(Fingerprint, SemanticKey));
    }

    [Fact]
    public void Store_ReadyOverInQueue_ReplacesEntry()
    {
        var cache = CreateCache();
        var region = CreateRegion();
        cache.TryStoreInQueue(Fingerprint, SemanticKey);
        cache.Store(region, ReadyResult());

        Assert.True(cache.TryGetReady(Fingerprint, SemanticKey, out var ready));
        Assert.Equal(QueryTranslationStatus.Ready, ready!.Status);
    }

    [Fact]
    public void TryGetReady_DoesNotReturnInQueueEntries()
    {
        var cache = CreateCache();
        cache.TryStoreInQueue(Fingerprint, SemanticKey);
        Assert.False(cache.TryGetReady(Fingerprint, SemanticKey, out _));
    }

    [Fact]
    public void InQueueEntry_WithZeroTtl_IsImmediatelyExpired()
    {
        var cache = new HoverResultCache(hoverCacheTtlMs: 60_000, inQueueCacheTtlMs: 0);
        cache.TryStoreInQueue(Fingerprint, SemanticKey);
        Assert.False(cache.TryGetAny(Fingerprint, SemanticKey, out _));
    }

    [Fact]
    public void ReadyEntry_IsDurable_EvenWhenStoredLongAgo()
    {
        var cache = CreateCache();
        var region = CreateRegion();
        cache.Store(region, ReadyResult());
        InjectStaleEntry(cache, Fingerprint, SemanticKey, DateTime.UtcNow.AddDays(-7).Ticks, QueryTranslationStatus.Ready);

        Assert.True(cache.TryGetReady(Fingerprint, SemanticKey, out var ready));
        Assert.Equal(QueryTranslationStatus.Ready, ready!.Status);
    }

    [Fact]
    public void InQueueEntry_CreatedLongAgo_IsExpired()
    {
        var cache = new HoverResultCache(hoverCacheTtlMs: 60_000, inQueueCacheTtlMs: 3_000);
        InjectStaleEntry(cache, Fingerprint, SemanticKey, DateTime.UtcNow.AddMinutes(-5).Ticks, QueryTranslationStatus.InQueue);
        Assert.False(cache.TryGetAny(Fingerprint, SemanticKey, out _));
    }

    [Fact]
    public void IsSemanticKeyReady_ReturnsTrueForReadyEntry()
    {
        var cache = CreateCache();
        var region = CreateRegion();
        cache.Store(region, ReadyResult());
        Assert.True(cache.IsSemanticKeyReady(SemanticKey));
    }

    [Fact]
    public void Store_TranslationError_DoesNotCacheReady()
    {
        var cache = CreateCache();
        var region = CreateRegion();
        cache.TryStoreInQueue(Fingerprint, SemanticKey);

        var error = new HoverResult(
            QueryTranslationStatus.Ready,
            new Hover(),
            new QueryLensStructuredHoverResult(
                Success: false,
                ErrorMessage: "Uninitialized Strings cannot be created.",
                Statements: [],
                CommandCount: 0,
                SourceExpression: null,
                ExecutedExpression: null,
                DbContextType: null,
                ProviderName: null,
                SourceFile: null,
                SourceLine: 0,
                Warnings: [],
                EnrichedSql: null,
                Mode: null,
                Status: QueryTranslationStatus.Ready,
                StatusMessage: "Uninitialized Strings cannot be created.",
                AvgTranslationMs: 0));

        cache.Store(region, error);

        Assert.False(cache.TryGetReady(Fingerprint, SemanticKey, out _));
        Assert.False(cache.IsSemanticKeyReady(SemanticKey));
    }

    private static HoverResultCache CreateCache()
        => new(hoverCacheTtlMs: 15_000, inQueueCacheTtlMs: 3_000);

    private static QueryRegion CreateRegion()
        => new(SemanticKey, "region-key", Fingerprint, 1, 1, "db.Orders", "db");

    private static HoverResult ReadyResult()
        => new(QueryTranslationStatus.Ready, new Hover(), null);

    private static void InjectStaleEntry(
        HoverResultCache cache,
        string fingerprint,
        string semanticKey,
        long ticks,
        QueryTranslationStatus status)
    {
        var field = typeof(HoverResultCache).GetField(
            "_entries",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var dict = field.GetValue(cache)!;
        var key = HoverResultCache.BuildCacheKey(fingerprint, semanticKey);
        var entryType = typeof(HoverResultCache).GetNestedType("CachedEntry", System.Reflection.BindingFlags.NonPublic)!;
        var entry = Activator.CreateInstance(entryType, ticks, new Hover(), null, status)!;
        dict.GetType().GetProperty("Item")!.SetValue(dict, entry, [key]);
    }
}
