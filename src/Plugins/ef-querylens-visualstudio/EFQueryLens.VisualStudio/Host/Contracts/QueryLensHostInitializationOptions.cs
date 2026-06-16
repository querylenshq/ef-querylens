namespace EFQueryLens.VisualStudio.Host.Contracts;

/// <summary>
/// Client configuration sent under <c>initializationOptions.queryLens</c> and
/// <c>workspace/didChangeConfiguration.settings.queryLens</c>.
/// </summary>
internal sealed class QueryLensHostInitializationOptions
{
    public bool? DebugEnabled { get; set; }

    public bool? EnableLspHover { get; set; }

    public bool? HoverProgressNotify { get; set; }

    public bool? SqlReadyNotify { get; set; }

    public int? HoverProgressDelayMs { get; set; }

    public int? HoverCacheTtlMs { get; set; }

    public int? MarkdownQueueAdaptiveWaitMs { get; set; }

    public int? StructuredQueueAdaptiveWaitMs { get; set; }

    public int? WarmupSuccessTtlMs { get; set; }

    public int? WarmupFailureCooldownMs { get; set; }

    public int? HoverWaitWhenWarmMs { get; set; }

    public int? HoverForegroundResolveBudgetMs { get; set; }

    public bool? HoverFastProbeEnabled { get; set; }

    public object ToClientPayload() =>
        new
        {
            debugEnabled = DebugEnabled,
            enableLspHover = EnableLspHover,
            hoverProgressNotify = HoverProgressNotify,
            sqlReadyNotify = SqlReadyNotify,
            hoverProgressDelayMs = HoverProgressDelayMs,
            hoverCacheTtlMs = HoverCacheTtlMs,
            markdownQueueAdaptiveWaitMs = MarkdownQueueAdaptiveWaitMs,
            structuredQueueAdaptiveWaitMs = StructuredQueueAdaptiveWaitMs,
            warmupSuccessTtlMs = WarmupSuccessTtlMs,
            warmupFailureCooldownMs = WarmupFailureCooldownMs,
            hoverWaitWhenWarmMs = HoverWaitWhenWarmMs,
            hoverForegroundResolveBudgetMs = HoverForegroundResolveBudgetMs,
            hoverFastProbeEnabled = HoverFastProbeEnabled,
        };

    public static object WrapForInitialize(QueryLensHostInitializationOptions options) =>
        new { queryLens = options.ToClientPayload() };

    public static object WrapForConfigurationChange(QueryLensHostInitializationOptions options) =>
        new { settings = new { queryLens = options.ToClientPayload() } };
}
