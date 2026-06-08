using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Engine;
using EFQueryLens.Lsp.Handlers;
using EFQueryLens.Lsp.Services;

namespace EFQueryLens.Core.Tests.Lsp;

public class HoverHandlerInvalidationTests
{
    [Fact]
    public async Task InvalidateForConfigurationChange_AlsoInvalidatesDaemonCache()
    {
        var engine = new TrackingEngineControl();
        var handler = new HoverHandler(
            new DocumentManager(),
            new HoverPreviewService(engine),
            engine);

        handler.InvalidateForConfigurationChange();

        await engine.InvalidateAwaiter.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, engine.InvalidateCount);
    }

    [Fact]
    public async Task OnAssemblyChanged_AlsoInvalidatesDaemonCache()
    {
        var engine = new TrackingEngineControl();
        var handler = new HoverHandler(
            new DocumentManager(),
            new HoverPreviewService(engine),
            engine);

        handler.OnAssemblyChanged();

        await engine.InvalidateAwaiter.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, engine.InvalidateCount);
    }

    private sealed class TrackingEngineControl : IQueryLensEngine, IEngineControl
    {
        private readonly TaskCompletionSource _invalidateAwaiter = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int InvalidateCount { get; private set; }

        public Task InvalidateAwaiter => _invalidateAwaiter.Task;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task InvalidateAssemblyCachesAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<QueryTranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct = default)
            => Task.FromResult(new QueryTranslationResult());

        public Task<ModelSnapshot> InspectModelAsync(ModelInspectionRequest request, CancellationToken ct = default)
            => Task.FromResult(new ModelSnapshot { DbContextType = string.Empty });

        public Task PingAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task RestartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task InvalidateCacheAsync(CancellationToken ct = default)
        {
            InvalidateCount++;
            _invalidateAwaiter.TrySetResult();
            return Task.CompletedTask;
        }

        public Task WarmTranslateAsync(TranslationRequest request, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
