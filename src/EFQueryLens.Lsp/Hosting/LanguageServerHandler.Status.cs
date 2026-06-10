using EFQueryLens.Lsp.Services;
using StreamJsonRpc;

namespace EFQueryLens.Lsp.Hosting;

internal sealed partial class LanguageServerHandler
{
    private QueryLensStatusTracker? _statusTracker;
    private HoverReadyNotifier? _sqlReadyNotifier;

    internal void SetStatusTracker(QueryLensStatusTracker tracker)
    {
        _statusTracker = tracker;
        tracker.AttachRpc(JsonRpc);
    }

    internal void SetSqlReadyNotifier(HoverReadyNotifier notifier)
    {
        _sqlReadyNotifier = notifier;
        notifier.AttachRpc(JsonRpc);
    }

    [JsonRpcMethod("efquerylens/status", UseSingleObjectParameterDeserialization = true)]
    public QueryLensStatusSnapshot StatusAsync()
        => _statusTracker?.GetSnapshot()
           ?? new QueryLensStatusSnapshot(QueryLensHostState.Starting, "Starting QueryLens…");
}
