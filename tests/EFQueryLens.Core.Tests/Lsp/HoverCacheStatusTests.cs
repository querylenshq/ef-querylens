using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp.Handlers;
using EFQueryLens.Lsp.HoverPipeline;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Core.Tests.Lsp;

/// <summary>
/// Handler-level cache integration tests. Unit-level cache behaviour lives in
/// <see cref="HoverResultCacheTests"/>.
/// </summary>
public class HoverCacheStatusTests
{
    [Fact]
    public void Handler_IsSemanticKeyReady_ReflectsResultCache()
    {
        var handler = CreateHandler();
        var region = new QueryRegion("shared-key", "rk", "fp|1|1", 1, 1, "db.X", "db");
        handler.ResultCache.Store(region, new HoverResult(QueryTranslationStatus.Ready, new Hover(), null));

        Assert.True(handler.IsSemanticKeyReady("shared-key"));
        Assert.False(handler.IsSemanticKeyReady("other-key"));
    }

    [Fact]
    public void TranslationPrewarmService_DoesNotWireSemanticEvictionDelegate()
    {
        var hasEvictionParam = typeof(TranslationPrewarmService)
            .GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SelectMany(c => c.GetParameters())
            .Any(p => p.ParameterType == typeof(Action<IEnumerable<string>>));

        Assert.False(hasEvictionParam,
            "TranslationPrewarmService must not wire a semantic-cache eviction delegate — " +
            "per-file eviction drops keys other files still reuse.");
    }

    private static HoverHandler CreateHandler()
        => new(new EFQueryLens.Lsp.DocumentManager(), new HoverPreviewService(new NoOpQueryLensEngine()));

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
