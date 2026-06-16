using EFQueryLens.Lsp.Protocol;
using EFQueryLens.VisualStudio.Host.Contracts;
using Newtonsoft.Json.Linq;

namespace EFQueryLens.Core.Tests.VisualStudio;

public sealed class VsHostProtocolParityTests
{
    [Fact]
    public void HostInitializationOptions_MatchesProtocolInitializePayload()
    {
        var protocol = SampleProtocolInitOptions();
        var host = SampleHostInitOptions();

        var protocolJson = JObject.FromObject(QueryLensInitializationOptions.WrapForInitialize(protocol));
        var hostJson = JObject.FromObject(QueryLensHostInitializationOptions.WrapForInitialize(host));

        Assert.True(JToken.DeepEquals(protocolJson, hostJson));
    }

    [Fact]
    public void HostInitializationOptions_MatchesProtocolConfigurationPayload()
    {
        var protocol = SampleProtocolInitOptions();
        var host = SampleHostInitOptions();

        var protocolJson = JObject.FromObject(QueryLensInitializationOptions.WrapForConfigurationChange(protocol));
        var hostJson = JObject.FromObject(QueryLensHostInitializationOptions.WrapForConfigurationChange(host));

        Assert.True(JToken.DeepEquals(protocolJson, hostJson));
    }

    [Fact]
    public void HostStatusMapper_MatchesProtocolForCoreStates()
    {
        foreach (var state in new[]
                 {
                     EFQueryLens.Lsp.Protocol.QueryLensHostState.Starting,
                     EFQueryLens.Lsp.Protocol.QueryLensHostState.Warming,
                     EFQueryLens.Lsp.Protocol.QueryLensHostState.Computing,
                     EFQueryLens.Lsp.Protocol.QueryLensHostState.Ready,
                     EFQueryLens.Lsp.Protocol.QueryLensHostState.Unavailable,
                 })
        {
            var hostState = (EFQueryLens.VisualStudio.Host.Contracts.QueryLensHostState)(int)state;
            var protocol = QueryLensStatusMapper.Map(new QueryLensStatusSnapshot(state, "msg", warmed: state == EFQueryLens.Lsp.Protocol.QueryLensHostState.Ready));
            var host = QueryLensHostStatusMapper.Map(new QueryLensHostStatusSnapshot(hostState, "msg", warmed: hostState == EFQueryLens.VisualStudio.Host.Contracts.QueryLensHostState.Ready));

            Assert.Equal(protocol.Text, host.Text);
            Assert.Equal(protocol.DisplayText, host.DisplayText);
            Assert.Equal(protocol.Tooltip, host.Tooltip);
        }
    }

    [Fact]
    public void HostSqlReadyLogic_MatchesProtocolDedupeAndNotifyRules()
    {
        SqlReadyNotificationLogic.ResetDedupeForTests();
        QueryLensHostSqlReadyNotificationLogic.ResetDedupeForTests();

        var protocolPayload = new SqlReadyNotification("file:///a.cs", 1, 2, "a.cs", 1);
        var hostPayload = new QueryLensHostSqlReadyNotification("file:///a.cs", 1, 2, "a.cs", 1);
        const long now = 1_000_000L;

        Assert.Equal(
            SqlReadyNotificationLogic.ShouldShow(protocolPayload, enabled: true, nowMs: now),
            QueryLensHostSqlReadyNotificationLogic.ShouldShow(hostPayload, enabled: true, nowMs: now));

        Assert.Equal(
            SqlReadyNotificationLogic.ShouldShow(protocolPayload, enabled: true, nowMs: now + 1_000),
            QueryLensHostSqlReadyNotificationLogic.ShouldShow(hostPayload, enabled: true, nowMs: now + 1_000));

        Assert.Equal(
            SqlReadyNotificationLogic.BuildToastMessage(protocolPayload),
            QueryLensHostSqlReadyNotificationLogic.BuildToastMessage(hostPayload));

        var protocolNoSql = new SqlReadyNotification("file:///a.cs", 1, 2, "a.cs", 0);
        var hostNoSql = new QueryLensHostSqlReadyNotification("file:///a.cs", 1, 2, "a.cs", 0);
        Assert.False(SqlReadyNotificationLogic.ShouldNotify(protocolNoSql));
        Assert.False(QueryLensHostSqlReadyNotificationLogic.ShouldNotify(hostNoSql));
    }

    [Fact]
    public void HostLspMethods_MatchProtocolMethodNames()
    {
        Assert.Equal(LspProtocolMethods.StatusRequest, QueryLensHostLspMethods.StatusRequest);
        Assert.Equal(LspProtocolMethods.WarmupRequest, QueryLensHostLspMethods.WarmupRequest);
        Assert.Equal(LspProtocolMethods.SqlReadyNotification, QueryLensHostLspMethods.SqlReadyNotification);
        Assert.Equal(LspProtocolMethods.StatusChangedNotification, QueryLensHostLspMethods.StatusChangedNotification);
    }

    [Fact]
    public void HostSqlReadyNotification_DeserializesCamelCaseObjectPayload()
    {
        var payload = JObject.Parse(
            """
            {
              "fileUri": "file:///C:/proj/Queries.cs",
              "line": 12,
              "character": 8,
              "fileName": "Queries.cs",
              "commandCount": 2
            }
            """);

        var notification = QueryLensHostSqlReadyNotificationParser.Parse(payload);

        Assert.NotNull(notification);
        Assert.Equal("file:///C:/proj/Queries.cs", notification!.FileUri);
        Assert.Equal(12, notification.Line);
        Assert.Equal(8, notification.Character);
        Assert.Equal("Queries.cs", notification.FileName);
        Assert.Equal(2, notification.CommandCount);
    }

    [Fact]
    public void HostSqlReadyNotification_DeserializesArrayWrappedPayload()
    {
        var payload = JArray.Parse(
            """
            [
              {
                "fileUri": "file:///a.cs",
                "line": 1,
                "character": 2,
                "fileName": "a.cs",
                "commandCount": 1
              }
            ]
            """);

        var notification = QueryLensHostSqlReadyNotificationParser.Parse(payload);

        Assert.NotNull(notification);
        Assert.Equal("file:///a.cs", notification!.FileUri);
        Assert.Equal(1, notification.CommandCount);
    }

    [Fact]
    public void HostSqlReadyNotification_ClientSidePathRespectsDedupe()
    {
        QueryLensHostSqlReadyNotificationLogic.ResetDedupeForTests();

        var notification = new QueryLensHostSqlReadyNotification("file:///a.cs", 1, 2, "a.cs", 1);
        const long now = 2_000_000L;

        Assert.True(QueryLensHostSqlReadyNotificationLogic.ShouldShow(notification, enabled: true, nowMs: now));
        Assert.False(QueryLensHostSqlReadyNotificationLogic.ShouldShow(notification, enabled: true, nowMs: now + 500));
    }

    private static QueryLensInitializationOptions SampleProtocolInitOptions()
    {
        return new QueryLensInitializationOptions
        {
            DebugEnabled = true,
            EnableLspHover = false,
            HoverProgressNotify = false,
            SqlReadyNotify = true,
            HoverProgressDelayMs = 350,
            HoverCacheTtlMs = 15_000,
            MarkdownQueueAdaptiveWaitMs = 200,
            StructuredQueueAdaptiveWaitMs = 200,
            WarmupSuccessTtlMs = 60_000,
            WarmupFailureCooldownMs = 5_000,
            HoverWaitWhenWarmMs = 8_000,
        };
    }

    private static QueryLensHostInitializationOptions SampleHostInitOptions()
    {
        return new QueryLensHostInitializationOptions
        {
            DebugEnabled = true,
            EnableLspHover = false,
            HoverProgressNotify = false,
            SqlReadyNotify = true,
            HoverProgressDelayMs = 350,
            HoverCacheTtlMs = 15_000,
            MarkdownQueueAdaptiveWaitMs = 200,
            StructuredQueueAdaptiveWaitMs = 200,
            WarmupSuccessTtlMs = 60_000,
            WarmupFailureCooldownMs = 5_000,
            HoverWaitWhenWarmMs = 8_000,
        };
    }
}
