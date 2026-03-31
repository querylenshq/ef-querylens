using System.Collections.Concurrent;
using EFQueryLens.Core.Contracts;

namespace EFQueryLens.Lsp.Handlers;

internal sealed class HoverCacheManager
{
    private const int CacheMaxEntries = 2_000;
    private const int CacheTargetEntries = 1_600;

    internal bool TryGetCachedEntry(
        ConcurrentDictionary<string, HoverHandler.CachedEntry> hoverCache,
        int hoverCacheTtlMs,
        int inQueueCacheTtlMs,
        string cacheKey,
        out HoverHandler.CachedEntry? entry)
    {
        entry = null;
        if (hoverCacheTtlMs <= 0) return false;
        if (!hoverCache.TryGetValue(cacheKey, out var cached)) return false;
        if (IsExpired(cached, hoverCacheTtlMs, inQueueCacheTtlMs)) { hoverCache.TryRemove(cacheKey, out _); return false; }
        entry = cached;
        return true;
    }

    internal bool TryGetSemanticCachedEntry(
        ConcurrentDictionary<string, HoverHandler.CachedEntry> semanticHoverCache,
        int hoverCacheTtlMs,
        int inQueueCacheTtlMs,
        string semanticKey,
        out HoverHandler.CachedEntry? entry)
    {
        entry = null;
        if (hoverCacheTtlMs <= 0) return false;
        if (!semanticHoverCache.TryGetValue(semanticKey, out var cached)) return false;
        if (IsExpired(cached, hoverCacheTtlMs, inQueueCacheTtlMs)) { semanticHoverCache.TryRemove(semanticKey, out _); return false; }
        if (cached.Status is not QueryTranslationStatus.Ready) return false;
        entry = cached;
        return true;
    }

    internal bool TryPromoteSemanticEntryToPrimary(
        ConcurrentDictionary<string, HoverHandler.CachedEntry> hoverCache,
        string cacheKey,
        HoverHandler.CachedEntry semanticEntry)
    {
        return hoverCache.TryAdd(cacheKey, semanticEntry);
    }

    internal bool TryCacheEntryInQueue(
        ConcurrentDictionary<string, HoverHandler.CachedEntry> hoverCache,
        int hoverCacheTtlMs,
        string cacheKey,
        HoverHandler.ComputedEntry entry)
    {
        if (hoverCacheTtlMs <= 0) return false;
        var cached = new HoverHandler.CachedEntry(DateTime.UtcNow.Ticks, entry.Hover, entry.Structured, QueryTranslationStatus.InQueue);
        return hoverCache.TryAdd(cacheKey, cached);
    }

    internal void CacheEntry(
        ConcurrentDictionary<string, HoverHandler.CachedEntry> hoverCache,
        ConcurrentDictionary<string, HoverHandler.CachedEntry> semanticHoverCache,
        int hoverCacheTtlMs,
        string cacheKey,
        HoverHandler.ComputedEntry entry,
        string? semanticKey)
    {
        if (hoverCacheTtlMs <= 0) return;

        if (entry.Status is not (QueryTranslationStatus.Ready
            or QueryTranslationStatus.InQueue
            or QueryTranslationStatus.Starting
            or QueryTranslationStatus.DaemonUnavailable))
        {
            return;
        }

        var cached = new HoverHandler.CachedEntry(DateTime.UtcNow.Ticks, entry.Hover, entry.Structured, entry.Status);
        hoverCache[cacheKey] = cached;

        if (entry.Status is QueryTranslationStatus.Ready
            && !string.IsNullOrWhiteSpace(semanticKey)
            && (entry.Hover is not null || entry.Structured is not null))
        {
            semanticHoverCache[semanticKey] = cached;
        }

        TrimCacheIfNeeded(hoverCache, static e => e.CreatedAtTicks);
        TrimCacheIfNeeded(semanticHoverCache, static e => e.CreatedAtTicks);
    }

    internal void RemovePrimaryEntry(ConcurrentDictionary<string, HoverHandler.CachedEntry> hoverCache, string cacheKey)
    {
        hoverCache.TryRemove(cacheKey, out _);
    }

    internal void InvalidateAll(
        ConcurrentDictionary<string, HoverHandler.CachedEntry> hoverCache,
        ConcurrentDictionary<string, HoverHandler.CachedEntry> semanticHoverCache)
    {
        hoverCache.Clear();
        semanticHoverCache.Clear();
    }

    private static bool IsExpired(HoverHandler.CachedEntry entry, int hoverCacheTtlMs, int inQueueCacheTtlMs)
    {
        var ttlMs = entry.Status is QueryTranslationStatus.InQueue
            ? inQueueCacheTtlMs
            : hoverCacheTtlMs;

        return entry.CreatedAtTicks + TimeSpan.FromMilliseconds(ttlMs).Ticks <= DateTime.UtcNow.Ticks;
    }

    private static void TrimCacheIfNeeded<TValue>(
        ConcurrentDictionary<string, TValue> cache,
        Func<TValue, long> createdAtTicksAccessor)
    {
        if (cache.Count <= CacheMaxEntries)
        {
            return;
        }

        var removeCount = cache.Count - CacheTargetEntries;
        if (removeCount <= 0)
        {
            return;
        }

        var keysToRemove = cache
            .OrderBy(pair => createdAtTicksAccessor(pair.Value))
            .Take(removeCount)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in keysToRemove)
        {
            cache.TryRemove(key, out _);
        }
    }
}
