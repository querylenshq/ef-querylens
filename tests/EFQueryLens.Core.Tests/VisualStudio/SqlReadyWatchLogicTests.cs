using EFQueryLens.VisualStudio.Host.Contracts;

namespace EFQueryLens.Core.Tests.VisualStudio;

public sealed class SqlReadyWatchLogicTests
{
    [Theory]
    [InlineData(8000, 60_000)]
    [InlineData(30_000, 60_000)]
    [InlineData(90_000, 90_000)]
    [InlineData(200_000, 120_000)]
    public void WatchBudget_UsesMinimumSixtySecondsAndCapsAtOneTwenty(int inputMs, int expectedMs)
    {
        Assert.Equal(expectedMs, SqlReadyWatchBudget.ComputeNotificationWaitMs(inputMs));
    }

    [Fact]
    public void WatchKey_MatchesDedupeStyle()
    {
        var key = SqlReadyWatchKey.Build("file:///a.cs", 12, 8);
        Assert.Equal("file:///a.cs|12|8", key);
    }

    [Fact]
    public void TestNotificationBuilder_UsesActiveFileWhenProvided()
    {
        var notification = SqlReadyTestNotificationBuilder.Build(@"C:\proj\Queries.cs", 4, 2);

        Assert.Contains("Queries.cs", notification.FileUri, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Queries.cs", notification.FileName);
        Assert.Equal(4, notification.Line);
        Assert.Equal(2, notification.Character);
        Assert.Equal(1, notification.CommandCount);
    }

    [Fact]
    public void TestNotificationBuilder_FallsBackWhenPathMissing()
    {
        var notification = SqlReadyTestNotificationBuilder.Build(null, 1, 1);

        Assert.Equal("file:///Test.cs", notification.FileUri);
        Assert.Equal("Test.cs", notification.FileName);
    }

    [Fact]
    public void Parser_ReturnsNullForEmptyArray()
    {
        var payload = Newtonsoft.Json.Linq.JArray.Parse("[]");
        Assert.Null(QueryLensHostSqlReadyNotificationParser.Parse(payload));
    }
}
