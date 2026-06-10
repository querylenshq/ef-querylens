using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Handlers;
using EFQueryLens.Lsp.HoverPipeline;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Core.Tests.Lsp;

public class HoverHandlerDaemonEventTests
{
    [Fact]
    public void OnAssemblyChanged_InvalidatesAllHoverCaches()
    {
        var handler = CreateHandler();
        SeedCache(handler);
        Assert.True(GetCacheEntryCount(handler) > 0);

        handler.OnAssemblyChanged();

        Assert.Equal(0, GetCacheEntryCount(handler));
    }

    [Fact]
    public void InvalidateForManualRecalculate_InvalidatesAllHoverCaches()
    {
        var handler = CreateHandler();
        SeedCache(handler);

        handler.InvalidateForManualRecalculate();

        Assert.Equal(0, GetCacheEntryCount(handler));
    }

    private static HoverHandler CreateHandler()
        => new(new DocumentManager(), new HoverPreviewService(new NoOpQueryLensEngine()));

    private static void SeedCache(HoverHandler handler)
    {
        var region = new QueryRegion("sem", "rk", "fp|1|1", 1, 1, "db.X", "db");
        handler.ResultCache.Store(region, new HoverResult(QueryTranslationStatus.Ready, new Hover(), null));
    }

    private static int GetCacheEntryCount(HoverHandler handler)
    {
        var field = typeof(HoverResultCache).GetField(
            "_entries",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var dict = field.GetValue(handler.ResultCache)!;
        return (int)dict.GetType().GetProperty("Count")!.GetValue(dict)!;
    }

    private sealed class NoOpQueryLensEngine : IQueryLensEngine
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task InvalidateAssemblyCachesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<QueryTranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct = default)
            => Task.FromResult(new QueryTranslationResult());
        public Task<ModelSnapshot> InspectModelAsync(ModelInspectionRequest request, CancellationToken ct = default)
            => Task.FromResult(new ModelSnapshot { DbContextType = string.Empty });
    }
}
