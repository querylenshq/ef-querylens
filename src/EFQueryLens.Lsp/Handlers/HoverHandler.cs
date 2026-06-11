using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Engine;
using EFQueryLens.Lsp.HoverPipeline;
using EFQueryLens.Lsp.Parsing;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Lsp.Handlers;

internal sealed partial class HoverHandler
{
    private readonly DocumentManager _documentManager;
    private readonly HoverPreviewService _hoverPreviewService;
    private readonly IQueryLensEngine? _engine;
    private readonly HoverRequestCoordinator _coordinator;
    private WarmupHandler? _warmupHandler;
    private QueryLensStatusTracker? _statusTracker;
    private AssemblyChangeTracker? _assemblyChangeTracker;
    private IHoverReadyNotifier? _sqlReadyNotifier;
    private bool _debugEnabled;

    public HoverHandler(
        DocumentManager documentManager,
        HoverPreviewService hoverPreviewService,
        IQueryLensEngine? engine = null)
    {
        _documentManager = documentManager;
        _hoverPreviewService = hoverPreviewService;
        _engine = engine;

        var hoverCacheTtlMs = LspEnvironment.ReadInt("QUERYLENS_HOVER_CACHE_TTL_MS", fallback: 15_000, min: 0, max: 120_000);
        var inQueueCacheTtlMs = LspEnvironment.ReadInt("QUERYLENS_INQUEUE_CACHE_TTL_MS", fallback: 45_000, min: 0, max: 120_000);
        var hoverWaitBudgetMs = LspEnvironment.ReadInt("QUERYLENS_HOVER_WAIT_BUDGET_MS", fallback: 8_000, min: 0, max: 30_000);
        var hoverQueuedAdaptiveWaitMs = LspEnvironment.ReadInt("QUERYLENS_MARKDOWN_QUEUE_ADAPTIVE_WAIT_MS", fallback: 200, min: 0, max: 2_000);
        var structuredQueuedAdaptiveWaitMs = LspEnvironment.ReadInt("QUERYLENS_STRUCTURED_QUEUE_ADAPTIVE_WAIT_MS", fallback: 200, min: 0, max: 2_000);
        _debugEnabled = LspEnvironment.ReadBool("QUERYLENS_DEBUG", fallback: false);

        var chainCache = new DocumentLinqChainCache();
        var resolver = new QueryRegionResolver(chainCache, _debugEnabled ? LogHoverDebug : null);
        var cache = new HoverResultCache(hoverCacheTtlMs, inQueueCacheTtlMs);
        _coordinator = new HoverRequestCoordinator(
            hoverPreviewService,
            resolver,
            cache,
            chainCache,
            hoverWaitBudgetMs,
            hoverQueuedAdaptiveWaitMs,
            structuredQueuedAdaptiveWaitMs,
            _debugEnabled ? LogHoverDebug : null,
            LogHoverOperation);
    }

    internal void SetWarmupHandler(WarmupHandler warmupHandler)
    {
        _warmupHandler = warmupHandler;
        _coordinator.SetAssemblyWarmChecker(assemblyPath =>
            !string.IsNullOrWhiteSpace(assemblyPath)
            && _warmupHandler?.IsAssemblyReady(assemblyPath) == true);
    }

    internal void SetAssemblyChangeTracker(AssemblyChangeTracker assemblyChangeTracker)
        => _assemblyChangeTracker = assemblyChangeTracker;

    internal void SetStatusTracker(QueryLensStatusTracker statusTracker)
        => _statusTracker = statusTracker;

    internal void SetSqlReadyNotifier(IHoverReadyNotifier notifier)
    {
        _sqlReadyNotifier = notifier;
        _coordinator.SetSqlReadyNotifier(notifier);
    }

    public void OnAssemblyChanged() => InvalidateCaches("assembly-changed");

    public void InvalidateForManualRecalculate() => InvalidateCaches("manual-recalculate");

    public void InvalidateForConfigurationChange() => InvalidateCaches("configuration-changed");

    public void OnDocumentChanged(string filePath) => _coordinator.InvalidateDocumentChains(filePath);

    public void ApplyClientConfiguration(LspClientConfiguration configuration)
    {
        if (configuration.DebugEnabled.HasValue)
        {
            _debugEnabled = configuration.DebugEnabled.Value;
            _hoverPreviewService.SetDebugEnabled(_debugEnabled);
        }

        var hoverCacheTtlMs = configuration.HoverCacheTtlMs ?? LspEnvironment.ReadInt("QUERYLENS_HOVER_CACHE_TTL_MS", fallback: 15_000, min: 0, max: 120_000);
        var inQueueCacheTtlMs = LspEnvironment.ReadInt("QUERYLENS_INQUEUE_CACHE_TTL_MS", fallback: 45_000, min: 0, max: 120_000);
        var hoverWaitBudgetMs = configuration.HoverWaitWhenWarmMs ?? LspEnvironment.ReadInt("QUERYLENS_HOVER_WAIT_BUDGET_MS", fallback: 8_000, min: 0, max: 30_000);
        var hoverQueuedAdaptiveWaitMs = configuration.MarkdownQueueAdaptiveWaitMs ?? LspEnvironment.ReadInt("QUERYLENS_MARKDOWN_QUEUE_ADAPTIVE_WAIT_MS", fallback: 200, min: 0, max: 2_000);
        var structuredQueuedAdaptiveWaitMs = configuration.StructuredQueueAdaptiveWaitMs ?? LspEnvironment.ReadInt("QUERYLENS_STRUCTURED_QUEUE_ADAPTIVE_WAIT_MS", fallback: 200, min: 0, max: 2_000);

        _coordinator.Configure(
            hoverCacheTtlMs,
            inQueueCacheTtlMs,
            hoverWaitBudgetMs,
            hoverQueuedAdaptiveWaitMs,
            structuredQueuedAdaptiveWaitMs);

        if (configuration.SqlReadyNotify.HasValue && _sqlReadyNotifier is HoverReadyNotifier notifier)
        {
            notifier.Configure(configuration.SqlReadyNotify.Value);
        }
    }

    internal IReadOnlyList<string> BuildChainSemanticKeys(
        string filePath,
        string sourceText,
        IReadOnlyList<LinqChainInfo> chains)
        => _coordinator.BuildChainSemanticKeys(filePath, sourceText, chains);

    internal bool IsSemanticKeyReady(string semanticKey)
        => _coordinator.IsSemanticKeyReady(semanticKey);

    internal void StorePrewarmedEntry(
        string filePath,
        string sourceText,
        int line,
        int character,
        CombinedHoverResult combined)
        => _coordinator.StorePrewarmed(filePath, sourceText, line, character, combined);

    private void InvalidateCaches(string reason)
    {
        _coordinator.InvalidateAll();
        LogHoverDebug($"hover-cache-invalidated reason={reason}");

        if (_engine is IEngineControl control)
        {
            _ = InvalidateDaemonCacheAsync(control, reason);
        }
    }

    private static async Task InvalidateDaemonCacheAsync(IEngineControl control, string reason)
    {
        try
        {
            await control.InvalidateCacheAsync();
        }
        catch
        {
            Console.Error.WriteLine($"[QL-Hover] daemon-cache-invalidate-failed reason={reason}");
        }
    }

    private void LogHoverDebug(string message)
    {
        if (!_debugEnabled)
        {
            return;
        }

        Console.Error.WriteLine($"[QL-Hover] {message}");
    }

    private void LogHoverOperation(
        string filePath,
        int line,
        int character,
        QueryTranslationStatus status,
        bool cached,
        bool background = false)
    {
        var phase = background ? "bg" : cached ? "cache" : "sync";
        QueryLensOperationalLog.Info(
            $"hover-{phase} file={Path.GetFileName(filePath)} line={line} char={character} status={status}");
    }
}
