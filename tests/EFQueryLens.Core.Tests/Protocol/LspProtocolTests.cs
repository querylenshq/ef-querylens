using EFQueryLens.Lsp.Protocol;

namespace EFQueryLens.Core.Tests.Protocol;

public sealed class LspProtocolTests
{
    [Fact]
    public void SqlReadyNotificationLogic_DedupesWithinWindow()
    {
        SqlReadyNotificationLogic.ResetDedupeForTests();
        var payload = new SqlReadyNotification("file:///a.cs", 1, 2, "a.cs", 1);
        var now = 1_000_000L;

        Assert.True(SqlReadyNotificationLogic.ShouldShow(payload, enabled: true, nowMs: now));
        Assert.False(SqlReadyNotificationLogic.ShouldShow(payload, enabled: true, nowMs: now + 1_000));
        Assert.True(SqlReadyNotificationLogic.ShouldShow(payload, enabled: true, nowMs: now + 31_000));
    }

    [Fact]
    public void SqlReadyNotificationLogic_RequiresCommandCount()
    {
        SqlReadyNotificationLogic.ResetDedupeForTests();
        var noSql = new SqlReadyNotification("file:///a.cs", 1, 2, "a.cs", 0);
        Assert.False(SqlReadyNotificationLogic.ShouldNotify(noSql));
        Assert.False(SqlReadyNotificationLogic.ShouldShow(noSql, enabled: true, nowMs: 1_000_000L));

        var withSql = new SqlReadyNotification("file:///a.cs", 1, 2, "a.cs", 1);
        Assert.True(SqlReadyNotificationLogic.ShouldNotify(withSql));
    }

    [Fact]
    public void SqlReadyNotificationLogic_RespectsDisabledFlag()
    {
        SqlReadyNotificationLogic.ResetDedupeForTests();
        var payload = new SqlReadyNotification("file:///a.cs", 1, 2, "a.cs", 1);
        Assert.False(SqlReadyNotificationLogic.ShouldShow(payload, enabled: false, nowMs: 1_000_000L));
    }

    [Fact]
    public void QueryLensStatusMapper_RequiresWarmedForReadyText()
    {
        var warming = QueryLensStatusMapper.Map(new QueryLensStatusSnapshot(
            QueryLensHostState.Ready,
            "Ready",
            warmed: false));

        Assert.Contains("Warming", warming.Text, StringComparison.Ordinal);

        var ready = QueryLensStatusMapper.Map(new QueryLensStatusSnapshot(
            QueryLensHostState.Ready,
            "Ready",
            warmed: true));

        Assert.Contains("Ready", ready.Text, StringComparison.Ordinal);
    }
}
