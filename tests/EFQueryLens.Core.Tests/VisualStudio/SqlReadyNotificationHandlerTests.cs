using EFQueryLens.VisualStudio.Host.Contracts;

namespace EFQueryLens.Core.Tests.VisualStudio;

public sealed class SqlReadyNotificationHandlerTests
{
    [Fact]
    public void TryPrepareShow_RespectsEnabledAndDedupe()
    {
        QueryLensHostSqlReadyNotificationLogic.ResetDedupeForTests();

        var notification = new QueryLensHostSqlReadyNotification("file:///a.cs", 1, 2, "a.cs", 1);
        const long now = 5_000_000L;

        Assert.True(SqlReadyNotificationHandlerLogic.TryPrepareShow(notification, enabled: true, nowMs: now, out var message));
        Assert.Contains("a.cs", message, StringComparison.Ordinal);

        Assert.False(SqlReadyNotificationHandlerLogic.TryPrepareShow(notification, enabled: true, nowMs: now + 500, out _));
        Assert.False(SqlReadyNotificationHandlerLogic.TryPrepareShow(notification, enabled: false, nowMs: now + 60_000, out _));
    }
}
