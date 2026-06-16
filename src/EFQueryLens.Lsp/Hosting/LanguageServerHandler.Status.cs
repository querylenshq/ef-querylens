using EFQueryLens.Lsp.Services;
using StreamJsonRpc;

using EFQueryLens.Lsp.Protocol;

namespace EFQueryLens.Lsp.Hosting;

internal sealed partial class LanguageServerHandler
{
    private QueryLensStatusTracker? _statusTracker;

    internal void SetStatusTracker(QueryLensStatusTracker tracker)
    {
        _statusTracker = tracker;
        tracker.AttachRpc(JsonRpc);
    }

    [JsonRpcMethod("efquerylens/status", UseSingleObjectParameterDeserialization = true)]
    public QueryLensStatusSnapshot StatusAsync()
        => _statusTracker?.GetSnapshot()
           ?? new QueryLensStatusSnapshot(QueryLensHostState.Starting, "Starting QueryLens…");
}
