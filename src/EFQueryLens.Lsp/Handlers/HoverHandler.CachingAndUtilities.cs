using EFQueryLens.Core;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Lsp.Handlers;

internal sealed partial class HoverHandler
{
    /// <summary>
    /// Looks up the primary position/fingerprint cache.
    /// Returns <c>true</c> for both <see cref="QueryTranslationStatus.Ready"/> and
    /// <see cref="QueryTranslationStatus.InQueue"/> entries so callers can render the
    /// appropriate status message immediately without re-triggering computation.
    /// </summary>
    private bool TryGetCachedEntry(string cacheKey, out CachedEntry? entry)
    {
        return _cacheManager.TryGetCachedEntry(
            _hoverCache,
            _hoverCacheTtlMs,
            _inQueueCacheTtlMs,
            cacheKey,
            out entry);
    }

    /// <summary>
    /// Looks up the semantic (expression-normalised) cache. Only returns
    /// <see cref="QueryTranslationStatus.Ready"/> entries — <c>InQueue</c> placeholders
    /// are intentionally excluded so they don't suppress real results at other cursor
    /// positions within the same chain.
    /// </summary>
    private bool TryGetSemanticCachedEntry(string semanticKey, out CachedEntry? entry)
    {
        return _cacheManager.TryGetSemanticCachedEntry(
            _semanticHoverCache,
            _hoverCacheTtlMs,
            _inQueueCacheTtlMs,
            semanticKey,
            out entry);
    }

    private bool IsCacheEnabled()
    {
        return _hoverCacheTtlMs > 0;
    }

    private bool TryPromoteSemanticToPrimaryCache(string cacheKey, CachedEntry semanticEntry)
    {
        return _cacheManager.TryPromoteSemanticEntryToPrimary(_hoverCache, cacheKey, semanticEntry);
    }

    private void RemovePrimaryCacheEntry(string cacheKey)
    {
        _cacheManager.RemovePrimaryEntry(_hoverCache, cacheKey);
    }

    private async Task<string?> GetSourceTextAsync(string documentUri, string filePath, CancellationToken cancellationToken)
    {
        var sourceText = _documentManager.GetDocumentText(documentUri);
        if (!string.IsNullOrWhiteSpace(sourceText))
        {
            return sourceText;
        }

        if (!File.Exists(filePath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(filePath, cancellationToken);
    }

    /// <summary>
    /// Atomically writes an <see cref="QueryTranslationStatus.InQueue"/> placeholder to the
    /// primary cache using <c>TryAdd</c>. Returns <c>true</c> if this call won the race
    /// and is responsible for starting the background compute task; <c>false</c> if another
    /// concurrent hover already wrote the placeholder (no second task should be started).
    /// </summary>
    private bool TryCacheEntryInQueue(string cacheKey, ComputedEntry entry)
    {
        return _cacheManager.TryCacheEntryInQueue(_hoverCache, _hoverCacheTtlMs, cacheKey, entry);
    }

    private void CacheEntry(string cacheKey, ComputedEntry entry, SemanticHoverContext? semanticContext)
    {
        _cacheManager.CacheEntry(
            _hoverCache,
            _semanticHoverCache,
            _hoverCacheTtlMs,
            cacheKey,
            entry,
            semanticContext?.SemanticKey);
    }

    private void InvalidateCaches(string reason)
    {
        _cacheManager.InvalidateAll(_hoverCache, _semanticHoverCache);
        LogHoverDebug($"hover-cache-invalidated reason={reason}");
    }

    private void LogHoverDebug(string message)
    {
        if (!_debugEnabled)
        {
            return;
        }

        Console.Error.WriteLine($"[QL-Hover] {message}");
    }

    internal sealed record CachedEntry(
        long CreatedAtTicks,
        Hover? Hover,
        QueryLensStructuredHoverResult? Structured,
        QueryTranslationStatus Status = QueryTranslationStatus.Ready);

    internal readonly record struct ComputedEntry(Hover? Hover, QueryLensStructuredHoverResult? Structured, QueryTranslationStatus Status);

}
