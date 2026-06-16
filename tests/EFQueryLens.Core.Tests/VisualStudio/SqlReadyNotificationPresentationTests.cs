using EFQueryLens.VisualStudio.Host.Contracts;

namespace EFQueryLens.Core.Tests.VisualStudio;

public sealed class SqlReadyNotificationPresentationTests
{
    [Fact]
    public void ShouldUseLayeredCues_MatchesNotifyEnabled()
    {
        Assert.True(SqlReadyNotificationPresentationLogic.ShouldUseLayeredCues(notifyEnabled: true));
        Assert.False(SqlReadyNotificationPresentationLogic.ShouldUseLayeredCues(notifyEnabled: false));
    }

    [Fact]
    public void BuildStatusBarMessage_MatchesToastMessage()
    {
        var notification = new QueryLensHostSqlReadyNotification("file:///a.cs", 4, 2, "Orders.cs", 1);

        Assert.Equal(
            QueryLensHostSqlReadyNotificationLogic.BuildToastMessage(notification),
            SqlReadyNotificationPresentationLogic.BuildStatusBarMessage(notification));
    }

    [Fact]
    public void BuildOutputLine_UsesConciseUserFacingFormat()
    {
        var notification = new QueryLensHostSqlReadyNotification("file:///a.cs", 4, 2, "Orders.cs", 1);

        Assert.Equal("SQL ready: Orders.cs:5", SqlReadyNotificationPresentationLogic.BuildOutputLine(notification));
    }

    [Fact]
    public void BuildOutputLine_FallsBackWhenFileNameMissing()
    {
        var notification = new QueryLensHostSqlReadyNotification("file:///a.cs", 0, 0, "", 1);

        Assert.Equal("SQL ready: query:1", SqlReadyNotificationPresentationLogic.BuildOutputLine(notification));
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(2, 2, true)]
    [InlineData(2, 1, false)]
    [InlineData(3, 1, false)]
    public void ShouldRestoreStatusBarFlash_OnlyRestoresLatestGeneration(
        int currentGeneration,
        int restoreGeneration,
        bool expected)
    {
        Assert.Equal(
            expected,
            SqlReadyNotificationPresentationLogic.ShouldRestoreStatusBarFlash(
                currentGeneration,
                restoreGeneration));
    }
}
