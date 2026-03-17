using System.Collections.Concurrent;
using EFQueryLens.Core.Grpc;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Lsp.Handlers;

internal sealed partial class HoverHandler
{
    private readonly DocumentManager _documentManager;
    private readonly HoverPreviewService _hoverPreviewService;
    private readonly ConcurrentDictionary<string, CachedHoverResult> _hoverCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedHoverResult> _semanticHoverCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<ComputedHover>>> _inflightSemanticHover = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedStructuredResult> _structuredHoverCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedStructuredResult> _semanticStructuredHoverCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<QueryLensStructuredHoverResult?>>> _inflightSemanticStructuredHover = new(StringComparer.OrdinalIgnoreCase);
    private int _hoverCacheTtlMs;
    private int _hoverCancellationGraceMs;
    private int _hoverQueuedAdaptiveWaitMs;
    private int _structuredQueuedAdaptiveWaitMs;
    private bool _debugEnabled;

    public HoverHandler(DocumentManager documentManager, HoverPreviewService hoverPreviewService)
    {
        _documentManager = documentManager;
        _hoverPreviewService = hoverPreviewService;
        _hoverCacheTtlMs = LspEnvironment.ReadInt(
            "QUERYLENS_HOVER_CACHE_TTL_MS",
            fallback: 15_000,
            min: 0,
            max: 120_000);
        _hoverCancellationGraceMs = LspEnvironment.ReadInt(
            "QUERYLENS_HOVER_CANCEL_GRACE_MS",
            fallback: 350,
            min: 0,
            max: 5_000);
        _hoverQueuedAdaptiveWaitMs = LspEnvironment.ReadInt(
            "QUERYLENS_MARKDOWN_QUEUE_ADAPTIVE_WAIT_MS",
            fallback: 200,
            min: 0,
            max: 2_000);
        _structuredQueuedAdaptiveWaitMs = LspEnvironment.ReadInt(
            "QUERYLENS_STRUCTURED_QUEUE_ADAPTIVE_WAIT_MS",
            fallback: 200,
            min: 0,
            max: 2_000);
        _debugEnabled = LspEnvironment.ReadBool("QUERYLENS_DEBUG", fallback: false);
    }

    public void HandleDaemonEvent(DaemonEvent daemonEvent)
    {
        switch (daemonEvent.EventCase)
        {
            case DaemonEvent.EventOneofCase.StateChanged:
                InvalidateCaches(
                    $"state-changed context={daemonEvent.StateChanged.ContextName} state={daemonEvent.StateChanged.State}");
                break;

            case DaemonEvent.EventOneofCase.ConfigReloaded:
                InvalidateCaches("config-reloaded");
                break;

            case DaemonEvent.EventOneofCase.AssemblyChanged:
                InvalidateCaches(
                    $"assembly-changed context={daemonEvent.AssemblyChanged.ContextName}");
                break;
        }
    }

    public void InvalidateForManualRecalculate()
    {
        InvalidateCaches("manual-recalculate");
    }

    public void InvalidateForConfigurationChange()
    {
        InvalidateCaches("configuration-changed");
    }

    public void ApplyClientConfiguration(LspClientConfiguration configuration)
    {
        if (configuration.DebugEnabled.HasValue)
        {
            _debugEnabled = configuration.DebugEnabled.Value;
            _hoverPreviewService.SetDebugEnabled(_debugEnabled);
        }

        if (configuration.HoverCacheTtlMs.HasValue)
        {
            _hoverCacheTtlMs = configuration.HoverCacheTtlMs.Value;
        }

        if (configuration.HoverCancelGraceMs.HasValue)
        {
            _hoverCancellationGraceMs = configuration.HoverCancelGraceMs.Value;
        }

        if (configuration.MarkdownQueueAdaptiveWaitMs.HasValue)
        {
            _hoverQueuedAdaptiveWaitMs = configuration.MarkdownQueueAdaptiveWaitMs.Value;
        }

        if (configuration.StructuredQueueAdaptiveWaitMs.HasValue)
        {
            _structuredQueuedAdaptiveWaitMs = configuration.StructuredQueueAdaptiveWaitMs.Value;
        }
    }
}
