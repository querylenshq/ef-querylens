using System.Diagnostics;
using EFQueryLens.Core;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Lsp.Handlers;

internal sealed partial class HoverHandler
{
    public async Task<QueryLensStructuredHoverResult?> HandleStructuredAsync(TextDocumentPositionParams request, CancellationToken cancellationToken)
    {
        var context = await TryCreateRequestContextAsync(
            request,
            cancellationToken,
            requestLogPrefix: "structured-hover-request",
            logNormalization: false);
        if (context is null)
        {
            return null;
        }

        var filePath = context.FilePath;
        var sourceText = context.SourceText;
        var semanticContext = context.SemanticContext;
        var effectiveLine = context.EffectiveLine;
        var effectiveCharacter = context.EffectiveCharacter;
        var cacheKey = context.CacheKey;

        if (TryGetCachedStructured(cacheKey, out var cachedResult))
        {
            LogHoverDebug($"structured-hover-cache-hit line={effectiveLine} char={effectiveCharacter}");
            return cachedResult;
        }

        if (semanticContext is not null
            && _hoverCacheTtlMs > 0
            && _semanticHoverCache.TryGetValue(semanticContext.SemanticKey, out var semEntry)
            && !IsExpired(semEntry)
            && semEntry.Structured is not null)
        {
            LogHoverDebug($"structured-hover-semantic-cache-hit line={effectiveLine} char={effectiveCharacter}");
            _hoverCache.TryAdd(cacheKey, semEntry);
            return semEntry.Structured;
        }

        if (semanticContext is not null)
        {
            var inFlightKey = BuildInFlightKey(filePath, semanticContext);
            // Share the same inflight task as HandleAsync — one engine call serves both.
            var lazyTask = _inflightSemanticHover.GetOrAdd(
                inFlightKey,
                _ => new Lazy<Task<ComputedEntry>>(
                    () => ComputeAndCacheCombinedAsync(
                        inFlightKey,
                        cacheKey,
                        filePath,
                        sourceText,
                        semanticContext),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                var sharedTask = lazyTask.Value;
                var sharedResult = await WaitWithCancellationAsync(sharedTask, cancellationToken);
                CacheEntry(cacheKey, sharedResult, semanticContext);
                return sharedResult.Structured;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var sharedTask = lazyTask.Value;
                var (completed, salvagedResult) = await TryGetResultWithinGraceAsync(sharedTask, _hoverCancellationGraceMs);
                if (completed)
                {
                    CacheEntry(cacheKey, salvagedResult, semanticContext);
                    LogHoverDebug($"structured-hover-cancel-salvaged line={effectiveLine} char={effectiveCharacter}");
                    return salvagedResult.Structured;
                }

                if (_hoverCacheTtlMs > 0
                    && _semanticHoverCache.TryGetValue(semanticContext.SemanticKey, out var semEntryAfterCancel)
                    && !IsExpired(semEntryAfterCancel)
                    && semEntryAfterCancel.Structured is not null)
                {
                    _hoverCache.TryAdd(cacheKey, semEntryAfterCancel);
                    return semEntryAfterCancel.Structured;
                }

                return null;
            }
        }

        var sw = Stopwatch.StartNew();
        ComputedEntry computed;
        try
        {
            computed = await ComputeCombinedAsync(
                filePath,
                sourceText,
                effectiveLine,
                effectiveCharacter,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogHoverDebug($"structured-hover-canceled line={effectiveLine} char={effectiveCharacter}");
            return null;
        }

        sw.Stop();
        LogHoverDebug($"structured-hover-compute-finished line={effectiveLine} char={effectiveCharacter} elapsedMs={sw.ElapsedMilliseconds} hasResult={computed.Structured is not null}");
        CacheEntry(cacheKey, computed, semanticContext);
        return computed.Structured;
    }
}
