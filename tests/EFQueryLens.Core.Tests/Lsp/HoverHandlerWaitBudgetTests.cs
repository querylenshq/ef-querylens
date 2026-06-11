using EFQueryLens.Core;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Handlers;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Core.Tests.Lsp;

/// <summary>
/// Verifies the bounded synchronous hover wait: on a cache miss the first hover waits up to the
/// configured budget for the background compute, returning the resolved result instead of the
/// "computing…" placeholder when the compute finishes in time. A zero budget keeps the prior
/// immediate-placeholder behavior.
///
/// No real assembly exists for the temp document, so the compute resolves quickly to a
/// "could not locate compiled target assembly" result — a fast, deterministic non-placeholder
/// outcome that's ideal for asserting the wait behavior.
/// </summary>
public sealed class HoverHandlerWaitBudgetTests
{
    private const string SourceWithQuery = "var q = db.Orders.Where(o => o.Id == 1).ToList();";

    [Fact]
    public async Task HandleAsync_WithWaitBudget_FirstHoverReturnsResolvedResult_NotPlaceholder()
    {
        var (handler, request) = Setup(waitBudgetMs: 2_000);

        var hover = await handler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(hover);
        Assert.DoesNotContain("computing", Markdown(hover!), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_WithZeroWaitBudget_FirstHoverReturnsComputingPlaceholder()
    {
        var (handler, request) = Setup(waitBudgetMs: 0);

        var hover = await handler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(hover);
        Assert.Contains("translating", Markdown(hover!), StringComparison.OrdinalIgnoreCase);
    }

    private static (HoverHandler handler, TextDocumentPositionParams request) Setup(int waitBudgetMs)
    {
        var documentManager = new DocumentManager();
        var handler = new HoverHandler(documentManager, new HoverPreviewService(new NoOpQueryLensEngine()));
        handler.ConfigureCachesForTests(hoverCacheTtlMs: 15_000, inQueueCacheTtlMs: 3_000, hoverWaitBudgetMs: waitBudgetMs);

        // Use a real, existing directory so the assembly resolver's directory walk doesn't throw;
        // the .cs file itself need not exist (the source is served from the DocumentManager).
        var uri = new Uri(Path.Combine(Path.GetTempPath(), "ql-wait-test-repo.cs"));
        documentManager.UpdateDocument(uri.ToString(), SourceWithQuery);

        var request = new TextDocumentPositionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(0, 12), // inside "db.Orders"
        };

        return (handler, request);
    }

    private static string Markdown(Hover hover)
        => ((MarkupContent)hover.Contents.Value!).Value;

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
