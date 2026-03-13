using EFQueryLens.Core.Scripting;

namespace EFQueryLens.Core.Tests.Scripting;

public class DesignTimeDbContextFactoryTests
{
    [Fact]
    public void TryCreateQueryLensFactory_MultiContextExplicitFactory_ReturnsRequestedContextA()
    {
        var result = DesignTimeDbContextFactory.TryCreateQueryLensFactory(
            typeof(FakeContextA),
            [typeof(MultiContextFactory).Assembly],
            out var failureReason);

        Assert.NotNull(result);
        Assert.IsType<FakeContextA>(result);
        Assert.True(string.IsNullOrWhiteSpace(failureReason));
    }

    [Fact]
    public void TryCreateQueryLensFactory_MultiContextExplicitFactory_ReturnsRequestedContextB()
    {
        var result = DesignTimeDbContextFactory.TryCreateQueryLensFactory(
            typeof(FakeContextB),
            [typeof(MultiContextFactory).Assembly],
            out var failureReason);

        Assert.NotNull(result);
        Assert.IsType<FakeContextB>(result);
        Assert.True(string.IsNullOrWhiteSpace(failureReason));
    }

    private sealed class FakeContextA;

    private sealed class FakeContextB;

    private sealed class MultiContextFactory :
        IQueryLensDbContextFactory<FakeContextA>,
        IQueryLensDbContextFactory<FakeContextB>
    {
        FakeContextA IQueryLensDbContextFactory<FakeContextA>.CreateOfflineContext() => new();

        FakeContextB IQueryLensDbContextFactory<FakeContextB>.CreateOfflineContext() => new();
    }
}
