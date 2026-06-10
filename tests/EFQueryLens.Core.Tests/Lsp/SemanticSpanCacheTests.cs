using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Handlers;
using EFQueryLens.Lsp.Parsing;
using EFQueryLens.Lsp.Services;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class SemanticSpanCacheTests
{
    [Fact]
    public void TryGetEnclosingCallArgumentSpan_ReturnsSharedProjectionLambdaSpan()
    {
        var source = """
            var coreData = await service.GetApplicationByIdAsync(
                applicationId,
                a => new
                {
                    ProductOwners = a.PrProductOwners.Where(w => w.IsNotDeleted).ToList(),
                },
                ct);
            """;

        var (ownersLine, ownersCharacter) = FindPosition(source, "PrProductOwners");
        var (nestedLine, nestedCharacter) = FindPosition(source, "IsNotDeleted");

        Assert.True(LspSyntaxHelper.TryGetEnclosingCallArgumentSpan(
            source, ownersLine, ownersCharacter, out var ownersStart, out var ownersEnd));
        Assert.True(LspSyntaxHelper.TryGetEnclosingCallArgumentSpan(
            source, nestedLine, nestedCharacter, out var nestedStart, out var nestedEnd));
        Assert.Equal(ownersStart, nestedStart);
        Assert.Equal(ownersEnd, nestedEnd);
    }

    [Fact]
    public void RegionResolver_SecondPosition_ReusesRegisteredSpanKey()
    {
        var source = """
            public class ApplicationService
            {
                public async Task<TResult?> GetApplicationByIdAsync<TResult>(
                    Guid applicationId,
                    Expression<Func<Application, TResult>> expression,
                    CancellationToken ct)
                {
                    return await dbContext.Applications
                        .Where(w => w.ApplicationId == applicationId)
                        .Select(expression)
                        .SingleOrDefaultAsync(ct);
                }
            }

            public class Caller
            {
                public async Task Run(ApplicationService service, Guid applicationId, CancellationToken ct)
                {
                    await service.GetApplicationByIdAsync(
                        applicationId,
                        a => new
                        {
                            ProductOwners = a.PrProductOwners.Where(w => w.IsNotDeleted).ToList(),
                        },
                        ct);
                }
            }
            """;

        var handler = new HoverHandler(new DocumentManager(), new HoverPreviewService(new NoOpQueryLensEngine()));
        var first = FindPosition(source, "PrProductOwners");
        var second = FindPosition(source, "IsNotDeleted");

        var resolvedFirst = handler.RegionResolver.TryResolve("Caller.cs", source, first.line, first.character);
        Assert.True(resolvedFirst.Found);

        Assert.True(handler.RegionResolver.TryGetSemanticKeyByPosition(
            "Caller.cs", source, second.line, second.character, out var registeredKey));
        Assert.Equal(resolvedFirst.Region!.SemanticKey, registeredKey);
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
            => Task.FromResult(new QueryTranslationResult { Success = true, Sql = "SELECT 1" });
        public Task<ModelSnapshot> InspectModelAsync(ModelInspectionRequest request, CancellationToken ct = default)
            => Task.FromResult(new ModelSnapshot { DbContextType = "TestDbContext" });
    }
}
