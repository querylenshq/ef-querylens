using System.Reflection;
using EFQueryLens.Core.AssemblyContext;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Handlers;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class WarmupHandlerMultiDbContextTests
{
    [Fact]
    public void IsMultipleDbContextAmbiguity_DetectsTypedDiscoveryException()
    {
        var ex = new DbContextDiscoveryException(
            DbContextDiscoveryFailureKind.MultipleDbContextsFound,
            "Multiple DbContext types found in 'App.dll': A, B. Specify --context to disambiguate.");

        Assert.True(InvokeIsMultipleDbContextAmbiguity(ex));
    }

    [Fact]
    public void IsMultipleDbContextAmbiguity_DetectsHttpWrappedMessage()
    {
        var ex = new HttpRequestException(
            "Response status code does not indicate success: 500 (Internal Server Error).",
            new DbContextDiscoveryException(
                DbContextDiscoveryFailureKind.MultipleDbContextsFound,
                "Multiple DbContext types found in 'App.dll': A, B."));

        Assert.True(InvokeIsMultipleDbContextAmbiguity(ex));
    }

    [Fact]
    public async Task ExecuteWarmupAsync_MultiDbContext_ReturnsSkippedSuccess()
    {
        var handler = new WarmupHandler(new DocumentManager(), new ThrowingInspectEngine());
        var method = typeof(WarmupHandler).GetMethod(
            "ExecuteWarmupAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var task = (Task<WarmupResponse>)method.Invoke(
            handler,
            [@"C:\app\bin\Debug\net8.0\App.dll", null])!;
        var response = await task;

        Assert.True(response.Success);
        Assert.Equal("skipped-multi-dbcontext", response.Message);
    }

    private static bool InvokeIsMultipleDbContextAmbiguity(Exception ex)
    {
        var method = typeof(WarmupHandler).GetMethod(
            "IsMultipleDbContextAmbiguity",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, [ex])!;
    }

    private sealed class ThrowingInspectEngine : IQueryLensEngine
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task InvalidateAssemblyCachesAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<ModelSnapshot> InspectModelAsync(ModelInspectionRequest request, CancellationToken ct = default)
            => throw new DbContextDiscoveryException(
                DbContextDiscoveryFailureKind.MultipleDbContextsFound,
                "Multiple DbContext types found in 'App.dll': A, B. Specify --context to disambiguate.");

        public Task<QueryTranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct = default)
            => Task.FromResult(new QueryTranslationResult());
    }
}
