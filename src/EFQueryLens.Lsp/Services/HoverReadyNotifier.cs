using EFQueryLens.Lsp.Protocol;
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
            Console.Error.WriteLine("[QL-LSP] sql-ready-notify skipped: JsonRpc not attached");
            return;
        }

        try
        {
            Console.Error.WriteLine(
                $"[QL-LSP] sql-ready-notify sending file={notification.FileName} " +
                $"line={notification.Line} char={notification.Character} commands={notification.CommandCount}");
            await rpc.NotifyAsync(
                LspProtocolMethods.SqlReadyNotification,
                notification,
                cancellationToken);
        }
        catch
        {
            // Best-effort — client may have disconnected.
        }
    }
}
