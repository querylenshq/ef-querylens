using EFQueryLens.Lsp.HoverPipeline;
using StreamJsonRpc;

namespace EFQueryLens.Lsp.Services;

internal interface IHoverReadyNotifier
{
    bool IsEnabled { get; }

    ValueTask NotifyAsync(SqlReadyNotification notification, CancellationToken cancellationToken = default);
}

/// <summary>
/// Pushes <c>efquerylens/sqlReady</c> to IDE clients when a background hover translation completes.
/// </summary>
internal sealed class HoverReadyNotifier : IHoverReadyNotifier
{
    private JsonRpc? _rpc;
    private bool _enabled = true;

    public bool IsEnabled => _enabled;

    public void AttachRpc(JsonRpc? rpc) => _rpc = rpc;

    public void Configure(bool enabled) => _enabled = enabled;

    public async ValueTask NotifyAsync(SqlReadyNotification notification, CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return;
        }

        var rpc = _rpc;
        if (rpc is null)
        {
            return;
        }

        try
        {
            await rpc.NotifyAsync(
                "efquerylens/sqlReady",
                new
                {
                    fileUri = notification.FileUri,
                    line = notification.Line,
                    character = notification.Character,
                    fileName = notification.FileName,
                    commandCount = notification.CommandCount,
                },
                cancellationToken);
        }
        catch
        {
            // Best-effort — client may have disconnected.
        }
    }
}
