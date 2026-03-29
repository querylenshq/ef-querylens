using EFQueryLens.Core;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Lsp.Handlers;

internal sealed partial class HoverHandler
{
    private const int CacheMaxEntries = 2_000;
    private const int CacheTargetEntries = 1_600;

    /// <summary>
    /// Looks up the primary position/fingerprint cache.
    /// Returns <c>true</c> for both <see cref="QueryTranslationStatus.Ready"/> and
    /// <see cref="QueryTranslationStatus.InQueue"/> entries so callers can render the
    /// appropriate status message immediately without re-triggering computation.
    /// </summary>
    private bool TryGetCachedEntry(string cacheKey, out CachedEntry? entry)
    {
        entry = null;
        if (_hoverCacheTtlMs <= 0) return false;
        if (!_hoverCache.TryGetValue(cacheKey, out var cached)) return false;
        if (IsExpired(cached)) { _hoverCache.TryRemove(cacheKey, out _); return false; }
        entry = cached;
        return true;
    }

    /// <summary>
    /// Looks up the semantic (expression-normalised) cache. Only returns
    /// <see cref="QueryTranslationStatus.Ready"/> entries — <c>InQueue</c> placeholders
    /// are intentionally excluded so they don't suppress real results at other cursor
    /// positions within the same chain.
    /// </summary>
    private bool TryGetSemanticCachedEntry(string semanticKey, out CachedEntry? entry)
    {
        entry = null;
        if (_hoverCacheTtlMs <= 0) return false;
        if (!_semanticHoverCache.TryGetValue(semanticKey, out var cached)) return false;
        if (IsExpired(cached)) { _semanticHoverCache.TryRemove(semanticKey, out _); return false; }
        if (cached.Status is not QueryTranslationStatus.Ready) return false;
        entry = cached;
        return true;
    }

    private bool IsExpired(CachedEntry entry)
    {
        // InQueue placeholders use a shorter TTL so a stale "computing..." message
        // doesn't linger if the background task silently failed or was never started.
        var ttlMs = entry.Status is QueryTranslationStatus.InQueue
            ? _inQueueCacheTtlMs
            : _hoverCacheTtlMs;

        return entry.CreatedAtTicks + TimeSpan.FromMilliseconds(ttlMs).Ticks <= DateTime.UtcNow.Ticks;
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

    private static async Task<T> WaitWithCancellationAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return await task;
        }

        if (task.IsCompleted)
        {
            return await task;
        }

        var cancellationTaskSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            cancellationTaskSource);

        var completed = await Task.WhenAny(task, cancellationTaskSource.Task);
        if (completed != task)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return await task;
    }

    private static async Task<(bool Completed, T? Result)> TryGetResultWithinGraceAsync<T>(Task<T> task, int graceMilliseconds)
    {
        if (graceMilliseconds <= 0)
        {
            return (false, default);
        }

        if (task.IsCompleted)
        {
            try
            {
                return (true, await task);
            }
            catch
            {
                return (false, default);
            }
        }

        var completed = await Task.WhenAny(task, Task.Delay(graceMilliseconds));
        if (completed != task)
        {
            return (false, default);
        }

        try
        {
            return (true, await task);
        }
        catch
        {
            return (false, default);
        }
    }

    private void CacheEntry(string cacheKey, ComputedEntry entry, SemanticHoverContext? semanticContext)
    {
        if (_hoverCacheTtlMs <= 0) return;

        // Store Ready and InQueue entries. All other statuses (errors, DaemonUnavailable, etc.)
        // are not cached — each hover retries independently for those.
        if (entry.Status is not (QueryTranslationStatus.Ready or QueryTranslationStatus.InQueue))
        {
            return;
        }

        var cached = new CachedEntry(DateTime.UtcNow.Ticks, entry.Hover, entry.Structured, entry.Status);
        _hoverCache[cacheKey] = cached;

        // InQueue placeholders go only to the primary cache — never the semantic cache.
        // The semantic cache is exclusively for cross-position deduplication of real results,
        // and an InQueue entry there would suppress valid results at adjacent cursor positions.
        if (entry.Status is QueryTranslationStatus.Ready
            && semanticContext is not null
            && (entry.Hover is not null || entry.Structured is not null))
        {
            _semanticHoverCache[semanticContext.SemanticKey] = cached;
        }

        TrimCacheIfNeeded(_hoverCache, static e => e.CreatedAtTicks);
        TrimCacheIfNeeded(_semanticHoverCache, static e => e.CreatedAtTicks);
    }

    private static void TrimCacheIfNeeded<TValue>(
        System.Collections.Concurrent.ConcurrentDictionary<string, TValue> cache,
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

    private void InvalidateCaches(string reason)
    {
        _hoverCache.Clear();
        _semanticHoverCache.Clear();
        _inflightSemanticHover.Clear();

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

    private sealed record CachedEntry(
        long CreatedAtTicks,
        Hover? Hover,
        QueryLensStructuredHoverResult? Structured,
        QueryTranslationStatus Status = QueryTranslationStatus.Ready);

    private readonly record struct ComputedEntry(Hover? Hover, QueryLensStructuredHoverResult? Structured, QueryTranslationStatus Status);
}
