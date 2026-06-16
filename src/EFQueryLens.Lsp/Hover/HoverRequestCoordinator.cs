using System.Collections.Concurrent;
using EFQueryLens.Core;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Parsing;
using EFQueryLens.Lsp.Services;

namespace EFQueryLens.Lsp.HoverPipeline;

/// <summary>
/// Single orchestration point for hover requests: region resolve, cache lookup,
/// inflight deduplication, translation, and bounded synchronous wait.
/// </summary>
internal sealed class HoverRequestCoordinator
{
    private readonly HoverPreviewService _previewService;
    private readonly QueryRegionResolver _resolver;
    private readonly HoverResultCache _cache;
    private readonly DocumentLinqChainCache _chainCache;
    private readonly ConcurrentDictionary<string, Lazy<Task<HoverResult>>> _pipelineInflight = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Action<string>? _log;
    private readonly Action<string, int, int, QueryTranslationStatus, bool, bool>? _logOperation;

    private int _hoverWaitBudgetMs;
    private int _foregroundResolveBudgetMs;
    private bool _fastProbeEnabled;
    private int _hoverQueuedAdaptiveWaitMs;
    private int _structuredQueuedAdaptiveWaitMs;
    private Func<string, bool>? _isAssemblyWarm;
    private Action<string>? _markAssemblyWarm;
    private Action<string>? _markAssemblyWarming;

    public HoverRequestCoordinator(
        HoverPreviewService previewService,
        QueryRegionResolver resolver,
        HoverResultCache cache,
        DocumentLinqChainCache chainCache,
        int hoverWaitBudgetMs,
        int foregroundResolveBudgetMs,
        bool fastProbeEnabled,
        int hoverQueuedAdaptiveWaitMs,
        int structuredQueuedAdaptiveWaitMs,
        Action<string>? log = null,
        Action<string, int, int, QueryTranslationStatus, bool, bool>? logOperation = null
    )
    {
        _previewService = previewService;
        _resolver = resolver;
        _cache = cache;
        _chainCache = chainCache;
        _hoverWaitBudgetMs = hoverWaitBudgetMs;
        _foregroundResolveBudgetMs = foregroundResolveBudgetMs;
        _fastProbeEnabled = fastProbeEnabled;
        _hoverQueuedAdaptiveWaitMs = hoverQueuedAdaptiveWaitMs;
        _structuredQueuedAdaptiveWaitMs = structuredQueuedAdaptiveWaitMs;
        _log = log;
        _logOperation = logOperation;
    }

    public DocumentLinqChainCache ChainCache => _chainCache;

    public QueryRegionResolver Resolver => _resolver;

    public HoverResultCache Cache => _cache;

    public void Configure(
        int hoverCacheTtlMs,
        int inQueueCacheTtlMs,
        int hoverWaitBudgetMs,
        int foregroundResolveBudgetMs,
        bool fastProbeEnabled,
        int hoverQueuedAdaptiveWaitMs,
        int structuredQueuedAdaptiveWaitMs
    )
    {
        _cache.Configure(hoverCacheTtlMs, inQueueCacheTtlMs);
        _hoverWaitBudgetMs = hoverWaitBudgetMs;
        _foregroundResolveBudgetMs = foregroundResolveBudgetMs;
        _fastProbeEnabled = fastProbeEnabled;
        _hoverQueuedAdaptiveWaitMs = hoverQueuedAdaptiveWaitMs;
        _structuredQueuedAdaptiveWaitMs = structuredQueuedAdaptiveWaitMs;
    }

    public void SetAssemblyWarmChecker(Func<string, bool>? checker) => _isAssemblyWarm = checker;

    public void SetAssemblyWarmStateActions(Action<string>? markWarm, Action<string>? markWarming)
    {
        _markAssemblyWarm = markWarm;
        _markAssemblyWarming = markWarming;
    }

    public bool IsSemanticKeyReady(string semanticKey) => _cache.IsSemanticKeyReady(semanticKey);

    public IReadOnlyList<string> BuildChainSemanticKeys(
        string filePath,
        string sourceText,
        IReadOnlyList<LinqChainInfo> chains
    ) => _resolver.BuildChainSemanticKeys(filePath, sourceText, chains);

    public void InvalidateDocumentChains(string filePath) => InvalidateDocument(filePath);

    public void InvalidateDocument(string filePath)
    {
        foreach (var (assemblyFingerprint, semanticKey) in _resolver.InvalidateDocument(filePath))
        {
            _cache.Remove(assemblyFingerprint, semanticKey);
        }

        var normalizedPath = Path.GetFullPath(filePath);
        foreach (var pipelineKey in _pipelineInflight.Keys.ToList())
        {
            if (
                pipelineKey.StartsWith(normalizedPath + "|", StringComparison.OrdinalIgnoreCase)
            )
            {
                _pipelineInflight.TryRemove(pipelineKey, out _);
            }
        }

        _chainCache.Invalidate(filePath);
    }

    public void InvalidateAll()
    {
        _cache.Clear();
        _resolver.Clear();
        _chainCache.Clear();
        _pipelineInflight.Clear();
    }

    public void StorePrewarmed(
        string filePath,
        string sourceText,
        int line,
        int character,
        CombinedHoverResult combined
    )
    {
        if (!_cache.IsEnabled)
        {
            return;
        }

        var result = HoverFormatting.FromCombined(combined);
        if (!HoverFormatting.IsCacheableTranslation(result))
        {
            return;
        }

        var resolve = _resolver.TryResolve(filePath, sourceText, line, character);
        if (!resolve.Found || resolve.Region is null)
        {
            return;
        }

        if (
            _cache.TryGetReady(
                resolve.Region.AssemblyFingerprint,
                resolve.Region.SemanticKey,
                out _
            )
        )
        {
            _log?.Invoke($"prewarm-skip-existing line={line} char={character}");
            return;
        }

        _cache.Store(resolve.Region, result);
        _log?.Invoke($"prewarm-stored line={line} char={character}");
    }

    public async Task<HoverResult> RequestAsync(
        string filePath,
        string sourceText,
        int line,
        int character,
        CancellationToken cancellationToken,
        bool nonBlocking = false
    )
    {
        _log?.Invoke($"hover-request path={filePath} line={line} char={character}");

        if (
            _resolver.TryGetSemanticKeyByPosition(
                filePath,
                sourceText,
                line,
                character,
                out var spanSemanticKey
            )
            && !string.IsNullOrWhiteSpace(spanSemanticKey)
            && TryGetReadyForSemanticKey(filePath, spanSemanticKey!, out var spanHit)
        )
        {
            _log?.Invoke($"hover-span-cache-hit line={line} char={character}");
            LogOperation(filePath, line, character, spanHit!.Status, cached: true);
            return spanHit!;
        }

        if (!_cache.IsEnabled)
        {
            return await ComputeImmediateAsync(
                filePath,
                sourceText,
                line,
                character,
                cancellationToken
            );
        }

        if (
            _fastProbeEnabled
            && !_resolver.MightNeedFullResolve(filePath, sourceText, line, character)
        )
        {
            _log?.Invoke($"hover-foreground-fast-none line={line} char={character}");
            return NoQueryResult();
        }

        if (nonBlocking)
        {
            return QueueBackgroundAndReturnPlaceholder(filePath, sourceText, line, character);
        }

        var resolveTask = _resolver.TryResolveAsync(
            filePath,
            sourceText,
            line,
            character,
            cancellationToken
        );
        var resolve = await TryWaitForForegroundResolveAsync(
            resolveTask,
            filePath,
            line,
            character,
            cancellationToken
        );

        if (resolve is null)
        {
            return QueueBackgroundAndReturnPlaceholder(filePath, sourceText, line, character);
        }

        if (!resolve.Found || resolve.Region is null)
        {
            return NoQueryResult();
        }

        _markAssemblyWarming?.Invoke(ResolveAssemblyPath(filePath));

        if (
            _cache.TryGetReady(
                resolve.Region.AssemblyFingerprint,
                resolve.Region.SemanticKey,
                out var cachedRegion
            )
        )
        {
            LogOperation(filePath, line, character, cachedRegion!.Status, cached: true);
            return cachedRegion!;
        }

        var pipeline = GetOrStartPipeline(
            resolve.Region.RegionKey,
            filePath,
            sourceText,
            line,
            character,
            out var isOwner
        );
        LogPipelineState(line, character, isOwner);

        if (_hoverWaitBudgetMs > 0 && ShouldWaitSynchronously(filePath))
        {
            var finished = await Task.WhenAny(
                pipeline,
                Task.Delay(_hoverWaitBudgetMs, cancellationToken)
            );
            if (finished == pipeline)
            {
                var waited = await pipeline.ConfigureAwait(false);
                if (HoverFormatting.IsResolvedForSync(waited))
                {
                    _log?.Invoke(
                        $"hover-wait-resolved line={line} char={character} status={waited.Status}"
                    );
                    LogOperation(filePath, line, character, waited.Status, cached: false);
                    return waited;
                }
            }
        }

        LogOperation(filePath, line, character, QueryTranslationStatus.InQueue, cached: false);
        return HoverFormatting.InQueuePlaceholder();
    }

    private HoverResult QueueBackgroundAndReturnPlaceholder(
        string filePath,
        string sourceText,
        int line,
        int character
    )
    {
        var regionKey = QueryRegionResolver.BuildRegionInflightKey(
            filePath,
            sourceText,
            line,
            character
        );
        var pipeline = GetOrStartPipeline(
            regionKey,
            filePath,
            sourceText,
            line,
            character,
            out var isOwner
        );
        _ = pipeline;
        LogPipelineState(line, character, isOwner);
        _log?.Invoke($"hover-foreground-fast-return line={line} char={character}");
        LogOperation(filePath, line, character, QueryTranslationStatus.InQueue, cached: false);
        return HoverFormatting.InQueuePlaceholder();
    }

    private async Task<QueryRegionResolver.RegionResolveResult?> TryWaitForForegroundResolveAsync(
        Task<QueryRegionResolver.RegionResolveResult> resolveTask,
        string filePath,
        int line,
        int character,
        CancellationToken cancellationToken
    )
    {
        if (_foregroundResolveBudgetMs <= 0)
        {
            _log?.Invoke($"hover-foreground-resolve-skipped line={line} char={character}");
            return null;
        }

        var finished = await Task.WhenAny(
            resolveTask,
            Task.Delay(_foregroundResolveBudgetMs, cancellationToken)
        );
        if (finished == resolveTask)
        {
            _log?.Invoke($"hover-foreground-resolve-completed line={line} char={character}");
            return await resolveTask.ConfigureAwait(false);
        }

        _log?.Invoke(
            $"hover-foreground-resolve-timeout line={line} char={character} budgetMs={_foregroundResolveBudgetMs}"
        );
        return null;
    }

    private static HoverResult NoQueryResult() => new(QueryTranslationStatus.Ready, null, null);

    private void LogPipelineState(int line, int character, bool isOwner)
    {
        if (isOwner)
        {
            _log?.Invoke($"hover-pipeline-queued line={line} char={character}");
        }
        else
        {
            _log?.Invoke($"hover-pipeline-join line={line} char={character}");
        }
    }

    private static string ResolveAssemblyPath(string filePath)
    {
        var assembly = AssemblyResolver.TryGetTargetAssembly(filePath);
        return string.IsNullOrWhiteSpace(assembly) ? string.Empty : assembly;
    }

    private Task<HoverResult> GetOrStartPipeline(
        string regionKey,
        string filePath,
        string sourceText,
        int line,
        int character,
        out bool isOwner
    )
    {
        var created = new Lazy<Task<HoverResult>>(
            () => RunPipelineAsync(filePath, sourceText, line, character),
            LazyThreadSafetyMode.ExecutionAndPublication
        );
        var inflight = _pipelineInflight.GetOrAdd(regionKey, created);
        isOwner = ReferenceEquals(inflight, created);

        if (isOwner)
        {
            _ = inflight.Value.ContinueWith(
                _ => _pipelineInflight.TryRemove(regionKey, out Lazy<Task<HoverResult>>? _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }

        return inflight.Value;
    }

    private async Task<HoverResult> RunPipelineAsync(
        string filePath,
        string sourceText,
        int line,
        int character
    )
    {
        try
        {
            var resolve = await _resolver
                .TryResolveAsync(filePath, sourceText, line, character, CancellationToken.None)
                .ConfigureAwait(false);

            if (resolve.Found && resolve.Region is not null)
            {
                var region = resolve.Region.WithRequestPosition(line, character);
                var assemblyPath = ResolveAssemblyPath(filePath);
                _markAssemblyWarming?.Invoke(assemblyPath);
                if (
                    _cache.TryGetReady(
                        region.AssemblyFingerprint,
                        region.SemanticKey,
                        out var cached
                    )
                )
                {
                    return cached!;
                }

                _cache.TryStoreInQueue(region.AssemblyFingerprint, region.SemanticKey);
                var computed = await ComputeForRegionAsync(
                        filePath,
                        sourceText,
                        region,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                _cache.Store(region, computed);
                if (HoverFormatting.IsCacheableTranslation(computed))
                {
                    _markAssemblyWarm?.Invoke(assemblyPath);
                }

                _log?.Invoke(
                    $"hover-pipeline-finished key={region.SemanticKey} status={computed.Status}"
                );
                LogOperation(
                    filePath,
                    line,
                    character,
                    computed.Status,
                    cached: false,
                    background: true
                );
                return computed;
            }

            var fallback = await ComputeImmediateAsync(
                    filePath,
                    sourceText,
                    line,
                    character,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            _log?.Invoke(
                $"hover-pipeline-finished line={line} char={character} status={fallback.Status} source=no-region"
            );
            LogOperation(
                filePath,
                line,
                character,
                fallback.Status,
                cached: false,
                background: true
            );
            return fallback;
        }
        catch (Exception ex)
        {
            _log?.Invoke(
                $"hover-pipeline-failed line={line} char={character} type={ex.GetType().Name} message={ex.Message}"
            );
            QueryLensOperationalLog.Info(
                $"hover-failed file={Path.GetFileName(filePath)} line={line} char={character} error={ex.GetType().Name}"
            );
            return HoverFormatting.InQueuePlaceholder();
        }
    }

    private async Task<HoverResult> ComputeForRegionAsync(
        string filePath,
        string sourceText,
        QueryRegion region,
        CancellationToken cancellationToken
    )
    {
        var combined = await _previewService.BuildCombinedAsync(
            filePath,
            sourceText,
            region.AnchorLine,
            region.AnchorCharacter,
            cancellationToken,
            region.Expression,
            region.ContextVariableName
        );

        combined = await ApplyAdaptiveWaitAsync(
            filePath,
            sourceText,
            region.AnchorLine,
            region.AnchorCharacter,
            combined,
            cancellationToken,
            region.Expression,
            region.ContextVariableName
        );

        return HoverFormatting.FromCombined(combined);
    }

    private async Task<HoverResult> ComputeImmediateAsync(
        string filePath,
        string sourceText,
        int line,
        int character,
        CancellationToken cancellationToken
    )
    {
        var combined = await _previewService.BuildCombinedAsync(
            filePath,
            sourceText,
            line,
            character,
            cancellationToken
        );

        combined = await ApplyAdaptiveWaitAsync(
            filePath,
            sourceText,
            line,
            character,
            combined,
            cancellationToken
        );
        return HoverFormatting.FromCombined(combined);
    }

    private async Task<CombinedHoverResult> ApplyAdaptiveWaitAsync(
        string filePath,
        string sourceText,
        int line,
        int character,
        CombinedHoverResult combined,
        CancellationToken cancellationToken,
        string? preresolvedExpression = null,
        string? preresolvedContextVariable = null
    )
    {
        var adaptiveWaitMs = Math.Max(_hoverQueuedAdaptiveWaitMs, _structuredQueuedAdaptiveWaitMs);
        if (
            combined.Markdown.Status
                is QueryTranslationStatus.InQueue
                    or QueryTranslationStatus.Starting
            && combined.Markdown.AvgTranslationMs > 0
            && combined.Markdown.AvgTranslationMs < adaptiveWaitMs
            && adaptiveWaitMs > 0
        )
        {
            _log?.Invoke(
                $"hover-adaptive-wait line={line} char={character} "
                    + $"waitMs={adaptiveWaitMs} avgMs={combined.Markdown.AvgTranslationMs:0.##}"
            );

            await Task.Delay(adaptiveWaitMs, cancellationToken);
            combined = await _previewService.BuildCombinedAsync(
                filePath,
                sourceText,
                line,
                character,
                cancellationToken,
                preresolvedExpression,
                preresolvedContextVariable
            );
        }

        return combined;
    }

    private bool TryGetReadyForSemanticKey(
        string filePath,
        string semanticKey,
        out HoverResult? result
    )
    {
        var fingerprint =
            AssemblyResolver.TryGetAssemblyFingerprint(filePath)
            ?? $"no-assembly|{Path.GetFullPath(filePath)}";
        return _cache.TryGetReady(fingerprint, semanticKey, out result);
    }

    private bool ShouldWaitSynchronously(string filePath)
    {
        var assembly = AssemblyResolver.TryGetTargetAssembly(filePath);
        return !string.IsNullOrWhiteSpace(assembly) && (_isAssemblyWarm?.Invoke(assembly) == true);
    }

    private void LogOperation(
        string filePath,
        int line,
        int character,
        QueryTranslationStatus status,
        bool cached,
        bool background = false
    )
    {
        _logOperation?.Invoke(filePath, line, character, status, cached, background);
    }
}
