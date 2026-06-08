using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp.Handlers;

namespace EFQueryLens.Core.Tests.Lsp;

/// <summary>
/// Verifies LSP semantic cache keys align with the daemon's translation cache key:
/// unchanged expressions with different translation inputs must not share a key.
/// </summary>
public class SemanticCacheKeyTests
{
    private static TranslationRequest BaseRequest() => new()
    {
        Expression = "db.Orders.Where(o => o.Id > pageSize)",
        AssemblyPath = @"C:\app\bin\Debug\net10.0\App.dll",
        DbContextTypeName = "AppDbContext",
        ContextVariableName = "db",
        AdditionalImports = ["System.Linq"],
        UsingAliases = new Dictionary<string, string>(StringComparer.Ordinal) { ["Enums"] = "My.Enums" },
        UsingStaticTypes = ["System.Math"],
        LocalVariableTypes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pageSize"] = "int",
        },
    };

    [Fact]
    public void TranslationCacheKey_DifferentLocalVariableTypes_ProduceDifferentKeys()
    {
        var requestA = BaseRequest();
        var requestB = BaseRequest() with
        {
            LocalVariableTypes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["pageSize"] = "long",
            },
        };

        Assert.NotEqual(TranslationCacheKey.Compute(requestA), TranslationCacheKey.Compute(requestB));
    }

    [Fact]
    public void TranslationCacheKey_DifferentAdditionalImports_ProduceDifferentKeys()
    {
        var requestA = BaseRequest();
        var requestB = BaseRequest() with
        {
            AdditionalImports = ["System.Linq", "System.Collections.Generic"],
        };

        Assert.NotEqual(TranslationCacheKey.Compute(requestA), TranslationCacheKey.Compute(requestB));
    }

    [Fact]
    public void BuildSemanticKey_MatchesTranslationCacheKeyCompute()
    {
        var request = BaseRequest();

        Assert.Equal(TranslationCacheKey.Compute(request), HoverHandler.BuildSemanticKey(request));
    }

    [Fact]
    public void SemanticCache_DifferentLocalTypes_DoNotShareReadyEntry()
    {
        var handler = new HoverHandler(
            new EFQueryLens.Lsp.DocumentManager(),
            new EFQueryLens.Lsp.Services.HoverPreviewService(new NoOpQueryLensEngine()));

        var requestA = BaseRequest();
        var requestB = BaseRequest() with
        {
            LocalVariableTypes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["pageSize"] = "long",
            },
        };

        var keyA = HoverHandler.BuildSemanticKey(requestA);
        var keyB = HoverHandler.BuildSemanticKey(requestB);

        Assert.NotEqual(keyA, keyB);

        SeedSemanticReadyEntry(handler, keyA);
        Assert.True(handler.IsSemanticKeyReady(keyA));
        Assert.False(handler.IsSemanticKeyReady(keyB));
    }

    private static void SeedSemanticReadyEntry(HoverHandler handler, string semanticKey)
    {
        var semanticContextType = typeof(HoverHandler).GetNestedType(
            "SemanticHoverContext",
            System.Reflection.BindingFlags.NonPublic)!;
        var semanticContext = Activator.CreateInstance(semanticContextType, semanticKey, 1, 1)!;

        var structuredResult = new EFQueryLens.Lsp.Services.QueryLensStructuredHoverResult(
            Success: true,
            ErrorMessage: null,
            Statements: [],
            CommandCount: 1,
            SourceExpression: "db.Orders",
            ExecutedExpression: null,
            DbContextType: "AppDb",
            ProviderName: "sqlite",
            SourceFile: null,
            SourceLine: 1,
            Warnings: [],
            EnrichedSql: null,
            Mode: "direct",
            Status: QueryTranslationStatus.Ready,
            StatusMessage: null,
            AvgTranslationMs: 0);

        var computedEntryType = typeof(HoverHandler).GetNestedType("ComputedEntry", System.Reflection.BindingFlags.NonPublic)!;
        var computedEntry = Activator.CreateInstance(
            computedEntryType,
            new Microsoft.VisualStudio.LanguageServer.Protocol.Hover(),
            structuredResult,
            QueryTranslationStatus.Ready)!;

        var cacheEntryMethod = typeof(HoverHandler).GetMethod(
            "CacheEntry",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        cacheEntryMethod.Invoke(handler, ["primary", computedEntry, semanticContext]);
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
