using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Handlers;
using EFQueryLens.Lsp.HoverPipeline;
using EFQueryLens.Lsp.Services;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class HoverRequestCoordinatorTests
{
    [Fact]
    public void RegionResolver_ConcurrentResolveOnSameStatement_DedupesInflightWork()
    {
        const string source = """
            var ids = await dbContext.Orders
                .Where(o => o.Id > 0)
                .ToListAsync();
            """;
        var resolver = new QueryRegionResolver(new DocumentLinqChainCache());
        var first = FindPosition(source, "dbContext");
        var second = FindPosition(source, "Where");

        var keyA = QueryRegionResolver.BuildRegionInflightKey("file.cs", source, first.line, first.character);
        var keyB = QueryRegionResolver.BuildRegionInflightKey("file.cs", source, second.line, second.character);
        Assert.Equal(keyA, keyB);

        var taskA = Task.Run(() => resolver.TryResolve("file.cs", source, first.line, first.character));
        var taskB = Task.Run(() => resolver.TryResolve("file.cs", source, second.line, second.character));
        Task.WaitAll(taskA, taskB);

        Assert.True(taskA.Result.Found);
        Assert.True(taskB.Result.Found);
        Assert.Equal(taskA.Result.Region!.SemanticKey, taskB.Result.Region!.SemanticKey);
    }

    [Fact]
    public void DocumentChainCache_InvalidatesOnDocumentChange()
    {
        var handler = new HoverHandler(new DocumentManager(), new HoverPreviewService(new NoOpQueryLensEngine()));
        const string path = "file.cs";
        const string source = "var q = db.Orders.ToList();";

        _ = handler.RegionResolver.TryResolve(path, source, 0, 8);
        handler.OnDocumentChanged(path);
        var chainsAfter = handler.RegionResolver;
        var result = chainsAfter.TryResolve(path, source, 0, 8);
        Assert.True(result.Found);
    }

    private static (int line, int character) FindPosition(string source, string marker)
    {
        var index = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0);
        var line = 0;
        var character = 0;
        for (var i = 0; i < index; i++)
        {
            if (source[i] == '\n')
            {
                line++;
                character = 0;
            }
            else
            {
                character++;
            }
        }

        return (line, character);
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
