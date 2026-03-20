using EFQueryLens.Core.Contracts;
using EFQueryLens.Core.Scripting;
using EFQueryLens.Core.Scripting.DesignTime;

namespace EFQueryLens.Core.Tests.Scripting;

public class DesignTimeDbContextFactoryTests
{
    [Fact]
    public void TryCreateEfDesignTimeFactory_WhenImplemented_ReturnsRequestedContext()
    {
        var result = DesignTimeDbContextFactory.TryCreateEfDesignTimeFactory(
            typeof(FakeContextA),
            [typeof(EfDesignTimeFactory).Assembly],
            out var failureReason);

        Assert.NotNull(result);
        Assert.IsType<FakeContextA>(result);
        Assert.True(string.IsNullOrWhiteSpace(failureReason));
    }

    private sealed class FakeContextA;

    private sealed class EfDesignTimeFactory :
        Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<FakeContextA>
    {
        public FakeContextA CreateDbContext(string[] args) => new();
    }
}
