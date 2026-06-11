using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp.HoverPipeline;
using EFQueryLens.Lsp.Services;

namespace EFQueryLens.Lsp.Handlers;

internal sealed partial class HoverHandler
{
    internal static string BuildSemanticKey(TranslationRequest request)
        => QueryRegionResolver.BuildSemanticKey(request);

    internal static QueryLensStructuredHoverResult BuildInQueueStructured()
        => HoverFormatting.BuildInQueueStructured();

    internal HoverResultCache ResultCache => _coordinator.Cache;

    internal QueryRegionResolver RegionResolver => _coordinator.Resolver;

    internal void ConfigureCachesForTests(
        int hoverCacheTtlMs,
        int inQueueCacheTtlMs,
        int hoverWaitBudgetMs = 8_000)
    {
        _coordinator.Configure(hoverCacheTtlMs, inQueueCacheTtlMs, hoverWaitBudgetMs, 200, 200);
    }

    internal void SetSqlReadyNotifierForTests(IHoverReadyNotifier notifier)
        => SetSqlReadyNotifier(notifier);
}
