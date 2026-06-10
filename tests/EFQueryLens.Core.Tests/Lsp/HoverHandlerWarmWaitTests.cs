using System.Reflection;
using EFQueryLens.Core;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Handlers;
using EFQueryLens.Lsp.Parsing;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class HoverHandlerWarmWaitTests
{
    private const string SourceWithQuery = "var q = db.Orders.Where(o => o.Id == 1).ToList();";

    [Fact]
    public async Task HandleAsync_ColdPathWithWaitBudget_ReturnsTranslatingPlaceholder()
    {
        var (handler, request) = Setup(waitBudgetMs: 2_000, assemblyWarmed: false);

        var hover = await handler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(hover);
        Assert.Contains("translating", Markdown(hover!), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_WarmPathWithWaitBudget_ReturnsResolvedResult_NotPlaceholder()
    {
        var sampleFile = Path.Combine(
            FindRepoRoot(),
            "samples",
            "SampleSqliteApp",
            "Application",
            "Customers",
            "CustomerReadService.cs");
        var targetAssembly = AssemblyResolver.TryGetTargetAssembly(sampleFile);
        Assert.False(string.IsNullOrWhiteSpace(targetAssembly));

        var (handler, request) = Setup(waitBudgetMs: 2_000, assemblyWarmed: true);

        var hover = await handler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(hover);
        Assert.DoesNotContain("translating", Markdown(hover!), StringComparison.OrdinalIgnoreCase);
    }

    private static (HoverHandler handler, TextDocumentPositionParams request) Setup(
        int waitBudgetMs,
        bool assemblyWarmed)
    {
        var documentManager = new DocumentManager();
        var warmup = new WarmupHandler(documentManager, new NoOpQueryLensEngine());
        var handler = new HoverHandler(documentManager, new HoverPreviewService(new NoOpQueryLensEngine()), new NoOpQueryLensEngine());
        handler.SetWarmupHandler(warmup);
        handler.ConfigureCachesForTests(hoverCacheTtlMs: 15_000, inQueueCacheTtlMs: 3_000, hoverWaitBudgetMs: waitBudgetMs);

        var sampleFile = Path.Combine(
            FindRepoRoot(),
            "samples",
            "SampleSqliteApp",
            "Application",
            "Customers",
            "CustomerReadService.cs");
        var uri = new Uri(sampleFile);
        documentManager.UpdateDocument(uri.ToString(), SourceWithQuery);

        if (assemblyWarmed)
        {
            var targetAssembly = AssemblyResolver.TryGetTargetAssembly(sampleFile);
            if (!string.IsNullOrWhiteSpace(targetAssembly))
            {
                SeedWarmupCache(warmup, targetAssembly);
            }
        }

        var request = new TextDocumentPositionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(0, 12),
        };

        return (handler, request);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "samples", "SampleSqliteApp")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for warm-wait tests.");
    }

    private static void SeedWarmupCache(WarmupHandler warmup, string assemblyPath)
    {
        var field = typeof(WarmupHandler).GetField("_warmCache", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var cacheType = field.FieldType;
        var cache = field.GetValue(warmup)!;
        var cachedWarmupType = typeof(WarmupHandler).GetNestedType("CachedWarmup", BindingFlags.NonPublic)!;
        var cached = Activator.CreateInstance(
            cachedWarmupType,
            DateTime.UtcNow.AddMinutes(5).Ticks,
            true,
            "ready")!;
        cacheType.GetMethod("set_Item")!.Invoke(cache, [assemblyPath, cached]);
    }

    private static string Markdown(Hover hover)
        => ((MarkupContent)hover.Contents.Value!).Value;

    private static void SetField(HoverHandler handler, string fieldName, object value)
    {
        var field = typeof(HoverHandler).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(handler, value);
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
