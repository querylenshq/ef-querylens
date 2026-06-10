using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp.Handlers;
using EFQueryLens.Lsp.HoverPipeline;
using EFQueryLens.Lsp.Services;

namespace EFQueryLens.Core.Tests.Lsp;

public class HoverHandlerNonBlockingTests
{
    [Fact]
    public void BuildInQueueStructured_StatusIsInQueue()
    {
        var result = HoverHandler.BuildInQueueStructured();
        Assert.Equal(QueryTranslationStatus.InQueue, result.Status);
    }

    [Fact]
    public void BuildInQueueStructured_SuccessIsFalse()
    {
        Assert.False(HoverHandler.BuildInQueueStructured().Success);
    }

    [Fact]
    public void BuildInQueueStructured_HasNonNullStatusMessage()
    {
        Assert.False(string.IsNullOrWhiteSpace(HoverHandler.BuildInQueueStructured().StatusMessage));
    }

    [Fact]
    public void BuildInQueueStructured_CommandCountIsZero()
    {
        Assert.Equal(0, HoverHandler.BuildInQueueStructured().CommandCount);
    }

    [Fact]
    public async Task RequestAsync_WithCacheDisabled_ComputesImmediatelyWithoutInQueuePlaceholder()
    {
        const string source = "var q = db.Orders.Where(o => o.Id == 1).ToList();";
        var documentManager = new EFQueryLens.Lsp.DocumentManager();
        var handler = new HoverHandler(documentManager, new HoverPreviewService(new NoOpQueryLensEngine()));
        handler.ConfigureCachesForTests(hoverCacheTtlMs: 0, inQueueCacheTtlMs: 3_000);

        var uri = new Uri(Path.Combine(Path.GetTempPath(), "ql-nonblocking.cs"));
        documentManager.UpdateDocument(uri.ToString(), source);

        var request = new Microsoft.VisualStudio.LanguageServer.Protocol.TextDocumentPositionParams
        {
            TextDocument = new Microsoft.VisualStudio.LanguageServer.Protocol.TextDocumentIdentifier { Uri = uri },
            Position = new Microsoft.VisualStudio.LanguageServer.Protocol.Position(0, 12),
        };

        var hover = await handler.HandleAsync(request, CancellationToken.None);
        Assert.NotNull(hover);
        Assert.DoesNotContain("translating", Markdown(hover!), StringComparison.OrdinalIgnoreCase);
    }

    private static string Markdown(Microsoft.VisualStudio.LanguageServer.Protocol.Hover hover)
        => ((Microsoft.VisualStudio.LanguageServer.Protocol.MarkupContent)hover.Contents.Value!).Value;

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
