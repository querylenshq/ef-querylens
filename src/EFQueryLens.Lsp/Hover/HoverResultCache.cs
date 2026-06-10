using EFQueryLens.Core;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Lsp.HoverPipeline;

internal sealed class HoverResultCache
{
    private const int CacheMaxEntries = 2_000;
    private const int CacheTargetEntries = 1_600;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CachedEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);
    private int _hoverCacheTtlMs;
    private int _inQueueCacheTtlMs;

    public HoverResultCache(int hoverCacheTtlMs, int inQueueCacheTtlMs)
    {
        _hoverCacheTtlMs = hoverCacheTtlMs;
        _inQueueCacheTtlMs = inQueueCacheTtlMs;
    }

    public void Configure(int hoverCacheTtlMs, int inQueueCacheTtlMs)
    {
        _hoverCacheTtlMs = hoverCacheTtlMs;
        _inQueueCacheTtlMs = inQueueCacheTtlMs;
    }

    public bool IsEnabled => _hoverCacheTtlMs > 0;

    public static string BuildCacheKey(string assemblyFingerprint, string semanticKey)
        => $"{assemblyFingerprint}|{semanticKey}";

    public bool TryGetReady(string assemblyFingerprint, string semanticKey, out HoverResult? result)
    {
        result = null;
        if (!IsEnabled)
        {
            return false;
        }

        var key = BuildCacheKey(assemblyFingerprint, semanticKey);
        if (!_entries.TryGetValue(key, out var cached) || IsExpired(cached))
        {
            if (cached is not null)
            {
                _entries.TryRemove(key, out _);
            }

            return false;
        }

        if (cached.Status is not QueryTranslationStatus.Ready || cached.Markdown is null)
        {
            return false;
        }

        result = ToHoverResult(cached, fromCache: true);
        return true;
    }

    public bool TryGetAny(string assemblyFingerprint, string semanticKey, out HoverResult? result)
    {
        result = null;
        if (!IsEnabled)
        {
            return false;
        }

        var key = BuildCacheKey(assemblyFingerprint, semanticKey);
        if (!_entries.TryGetValue(key, out var cached) || IsExpired(cached) || cached.Markdown is null)
        {
            if (cached is not null)
            {
                _entries.TryRemove(key, out _);
            }

            return false;
        }

        result = ToHoverResult(cached, fromCache: cached.Status is QueryTranslationStatus.Ready);
        return true;
    }

    public bool IsSemanticKeyReady(string semanticKey)
    {
        foreach (var pair in _entries)
        {
            if (!pair.Key.EndsWith($"|{semanticKey}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsExpired(pair.Value)
                && pair.Value.Status is QueryTranslationStatus.Ready
                && pair.Value.Markdown is not null)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryStoreInQueue(string assemblyFingerprint, string semanticKey)
    {
        if (!IsEnabled)
        {
            return false;
        }

        var key = BuildCacheKey(assemblyFingerprint, semanticKey);
        var placeholder = new CachedEntry(
            DateTime.UtcNow.Ticks,
            HoverFormatting.BuildInQueueHover(),
            HoverFormatting.BuildInQueueStructured(),
            QueryTranslationStatus.InQueue);
        return _entries.TryAdd(key, placeholder);
    }

    public void Store(QueryRegion region, HoverResult result)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (result.Status is not (QueryTranslationStatus.Ready
            or QueryTranslationStatus.InQueue
            or QueryTranslationStatus.Starting
            or QueryTranslationStatus.DaemonUnavailable))
        {
            return;
        }

        if (result.Markdown is null
            && result.Status is not (QueryTranslationStatus.InQueue or QueryTranslationStatus.Starting))
        {
            _entries.TryRemove(BuildCacheKey(region.AssemblyFingerprint, region.SemanticKey), out _);
            return;
        }

        var key = BuildCacheKey(region.AssemblyFingerprint, region.SemanticKey);
        if (result.Status is QueryTranslationStatus.Ready && !HoverFormatting.IsCacheableTranslation(result))
        {
            _entries.TryRemove(key, out _);
            return;
        }

        _entries[key] = new CachedEntry(
            DateTime.UtcNow.Ticks,
            result.Markdown,
            result.Structured,
            result.Status);
        TrimIfNeeded();
    }

    public void Remove(string assemblyFingerprint, string semanticKey)
    {
        _entries.TryRemove(BuildCacheKey(assemblyFingerprint, semanticKey), out _);
    }

    public void Clear() => _entries.Clear();

    private bool IsExpired(CachedEntry entry)
    {
        if (entry.Status is QueryTranslationStatus.Ready)
        {
            return false;
        }

        return entry.CreatedAtTicks + TimeSpan.FromMilliseconds(_inQueueCacheTtlMs).Ticks <= DateTime.UtcNow.Ticks;
    }

    private static HoverResult ToHoverResult(CachedEntry cached, bool fromCache)
        => new(cached.Status, cached.Markdown, cached.Structured, fromCache);

    private void TrimIfNeeded()
    {
        if (_entries.Count <= CacheMaxEntries)
        {
            return;
        }

        var removeCount = _entries.Count - CacheTargetEntries;
        if (removeCount <= 0)
        {
            return;
        }

        foreach (var key in _entries.OrderBy(pair => pair.Value.CreatedAtTicks).Take(removeCount).Select(pair => pair.Key))
        {
            _entries.TryRemove(key, out _);
        }
    }

    private sealed record CachedEntry(
        long CreatedAtTicks,
        Hover? Markdown,
        QueryLensStructuredHoverResult? Structured,
        QueryTranslationStatus Status);
}
