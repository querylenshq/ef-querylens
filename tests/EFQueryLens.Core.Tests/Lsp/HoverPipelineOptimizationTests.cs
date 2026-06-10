using System.Diagnostics;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Handlers;
using EFQueryLens.Lsp.HoverPipeline;
using EFQueryLens.Lsp.Parsing;
using EFQueryLens.Lsp.Services;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class HoverPipelineOptimizationTests
{
    [Fact]
    public void TryExtractLinqExpression_DirectDbContextChain_CompletesQuicklyWithoutCrossFileScan()
    {
        var source = """
            public sealed class DashboardPublisherService
            {
                private readonly MedicsApplicationDbContext dbContext;

                public async Task PublishAsync(Guid applicationId, CancellationToken ct)
                {
                    var row = await dbContext.Applications
                        .AsNoTracking()
                        .Where(w => w.ApplicationId == applicationId)
                        .Select(w => w.ApplicationId)
                        .SingleOrDefaultAsync(ct);
                }
            }
            """;

        var (line, character) = FindPosition(source, "AsNoTracking");

        var sw = Stopwatch.StartNew();
        var expression = LspSyntaxHelper.TryExtractLinqExpression(
            source,
            line,
            character,
            out var contextVariableName,
            sourceFilePath: @"D:\repo\src\Share.Medics.Applications.Core\Services\DashboardPublisherService.cs");
        sw.Stop();

        Assert.False(string.IsNullOrWhiteSpace(expression));
        Assert.Equal("dbContext", contextVariableName);
        Assert.True(sw.ElapsedMilliseconds < 2_000, $"extract-linq took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void RegionResolver_SecondPosition_ReusesSpanRegistryWithoutReextracting()
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
                        a => new { Owners = a.PrProductOwners.Where(w => w.IsNotDeleted).ToList() },
                        ct);
                }
            }
            """;

        var handler = new HoverHandler(new DocumentManager(), new HoverPreviewService(new NoOpQueryLensEngine()));
        var first = FindPosition(source, "PrProductOwners");
        var second = FindPosition(source, "IsNotDeleted");

        var firstResult = handler.RegionResolver.TryResolve("Caller.cs", source, first.line, first.character);
        Assert.True(firstResult.Found);
        Assert.NotNull(firstResult.Region);

        var sw = Stopwatch.StartNew();
        var secondResult = handler.RegionResolver.TryResolve("Caller.cs", source, second.line, second.character);
        sw.Stop();

        Assert.True(secondResult.Found);
        Assert.Equal(firstResult.Region!.SemanticKey, secondResult.Region!.SemanticKey);
        Assert.True(sw.ElapsedMilliseconds < 500, $"span-registry resolve took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void BuildRegionInflightKey_DbContextChain_PositionsShareStatementKey()
    {
        var source = """
            public sealed class DashboardPublisherService
            {
                public async Task PublishAsync(Guid applicationId, CancellationToken ct)
                {
                    var dealerLicenseIds = await dbContext
                        .ApplicationDlStatusChangeDetails.AsNoTracking()
                        .Where(x => x.IsNotDeleted && x.ApplicationId == applicationId)
                        .Select(x => x.DealerLicenseId)
                        .ToListAsync(ct);
                }
            }
            """;

        var dbContext = FindPosition(source, "dbContext");
        var asNoTracking = FindPosition(source, "AsNoTracking");
        var whereClause = FindPosition(source, "IsNotDeleted");

        var dbContextKey = QueryRegionResolver.BuildRegionInflightKey("DashboardPublisherService.cs", source, dbContext.line, dbContext.character);
        var asNoTrackingKey = QueryRegionResolver.BuildRegionInflightKey("DashboardPublisherService.cs", source, asNoTracking.line, asNoTracking.character);
        var whereKey = QueryRegionResolver.BuildRegionInflightKey("DashboardPublisherService.cs", source, whereClause.line, whereClause.character);

        Assert.Equal(dbContextKey, asNoTrackingKey);
        Assert.Equal(dbContextKey, whereKey);
        Assert.Contains("|stmt|", dbContextKey, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultCache_StorePromotesSemanticKeyForRehover()
    {
        var handler = new HoverHandler(new DocumentManager(), new HoverPreviewService(new NoOpQueryLensEngine()));
        var region = new QueryRegion("semantic-key-1", "rk", "no-assembly|DashboardPublisherService.cs", 10, 20, "db.X", "db");

        handler.ResultCache.TryStoreInQueue(region.AssemblyFingerprint, region.SemanticKey);
        handler.ResultCache.Store(region, new HoverResult(QueryTranslationStatus.Ready, new Microsoft.VisualStudio.LanguageServer.Protocol.Hover(), null));

        Assert.True(handler.ResultCache.TryGetReady(region.AssemblyFingerprint, region.SemanticKey, out var ready));
        Assert.Equal(QueryTranslationStatus.Ready, ready!.Status);
    }

    [Fact]
    public void TryGetEnclosingInvocationSpan_SharesSpanAcrossArgumentAndLambdaPositions()
    {
        var source = """
            await service.GetApplicationByIdAsync(
                applicationId,
                a => new { Owners = a.PrProductOwners.ToList() },
                ct);
            """;

        var (idLine, idChar) = FindPosition(source, "applicationId");
        var (lambdaLine, lambdaChar) = FindPosition(source, "PrProductOwners");

        Assert.True(LspSyntaxHelper.TryGetEnclosingInvocationSpan(source, idLine, idChar, out var idStart, out var idEnd));
        Assert.True(LspSyntaxHelper.TryGetEnclosingInvocationSpan(source, lambdaLine, lambdaChar, out var lambdaStart, out var lambdaEnd));
        Assert.Equal(idStart, lambdaStart);
        Assert.Equal(idEnd, lambdaEnd);
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
        public Task<ModelSnapshot> InspectModelAsync(ModelInspectionRequest request, CancellationToken ct = default)
            => Task.FromResult(new ModelSnapshot { DbContextType = string.Empty });
        public Task<QueryTranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct = default)
            => Task.FromResult(new QueryTranslationResult());
    }
}
