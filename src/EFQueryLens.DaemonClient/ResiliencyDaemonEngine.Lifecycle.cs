using EFQueryLens.Core;
using EFQueryLens.Core.Grpc;

namespace EFQueryLens.DaemonClient;

public sealed partial class ResiliencyDaemonEngine
{
    private async ValueTask DisposeCoreAsync()
    {
        _disposed = true;

        if (_shutdownDaemonOnDispose && _ownsDaemonLifecycle)
        {
            _debugLog?.Invoke("daemon-shutdown-on-dispose begin");
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _inner.ShutdownDaemonAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(2));
                _debugLog?.Invoke("daemon-shutdown-on-dispose completed");
            }
            catch (TimeoutException)
            {
                _debugLog?.Invoke("daemon-shutdown-on-dispose timeout");
            }
            catch (OperationCanceledException)
            {
                _debugLog?.Invoke("daemon-shutdown-on-dispose canceled");
            }
            catch (Exception ex)
            {
                _debugLog?.Invoke($"daemon-shutdown-on-dispose failed type={ex.GetType().Name} message={ex.Message}");
            }
        }

        await _inner.DisposeAsync();
        _reconnectGate.Dispose();
    }

    private async Task<bool> RestartDaemonCoreAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _reconnectGate.WaitAsync(ct);
        try
        {
            _debugLog?.Invoke($"daemon-restart requested workspace={_workspacePath}");

            try
            {
                using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                shutdownCts.CancelAfter(TimeSpan.FromSeconds(2));
                await _inner.ShutdownDaemonAsync(shutdownCts.Token);
            }
            catch (Exception ex)
            {
                _debugLog?.Invoke($"daemon-restart shutdown phase warning type={ex.GetType().Name} message={ex.Message}");
            }

            // Restart path is authoritative: current daemon is being shut down,
            // so reconnect must skip stale pid-file discovery and start fresh.
            await ReconnectCoreAsync(ct, forceFreshStart: true);
            _ownsDaemonLifecycle = true;
            _debugLog?.Invoke("daemon-restart success");
            return true;
        }
        finally
        {
            _reconnectGate.Release();
        }
    }

    private async Task RunDaemonEventSubscriptionCoreAsync(
        Action<DaemonEvent> onEvent,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(onEvent);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _inner.SubscribeAsync(onEvent, ct);
                if (!ct.IsCancellationRequested)
                {
                    _debugLog?.Invoke("daemon-subscribe-ended will-reconnect");
                    await ReconnectAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (IsDaemonTransportFailure(ex) && !ct.IsCancellationRequested)
            {
                _debugLog?.Invoke(
                    $"daemon-subscribe-failed will-reconnect type={ex.GetType().Name} message={ex.Message}");
                await ReconnectAsync(ct);
            }

            if (!ct.IsCancellationRequested)
            {
                await Task.Delay(150, ct);
            }
        }
    }
}
