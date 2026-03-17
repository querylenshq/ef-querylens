using Grpc.Core;
using System.Net.Sockets;

namespace EFQueryLens.DaemonClient;

public sealed partial class ResiliencyDaemonEngine
{
    private async Task<T> ExecuteWithReconnectAsync<T>(
        Func<DaemonBackedEngine, Task<T>> operation,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            return await operation(_inner);
        }
        catch (Exception ex) when (IsDaemonTransportFailure(ex) && !ct.IsCancellationRequested)
        {
            _debugLog?.Invoke($"daemon-transport-failure will-reconnect type={ex.GetType().Name} message={ex.Message}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _debugLog?.Invoke("daemon-connect-timeout will-reconnect");
        }

        await ReconnectAsync(ct);
        return await operation(_inner);
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        await _reconnectGate.WaitAsync(ct);
        try
        {
            await ReconnectCoreAsync(ct);
        }
        finally
        {
            _reconnectGate.Release();
        }
    }

    private async Task ReconnectCoreAsync(CancellationToken ct, bool forceFreshStart = false)
    {
        _debugLog?.Invoke($"daemon-reconnect workspace={_workspacePath}");

        var newPort = await DaemonLocator.TryGetOrStartDaemonAsync(
            _workspacePath,
            _daemonExecutablePath,
            _daemonAssemblyPath,
            timeoutMilliseconds: _startTimeoutMs,
            debugLog: _debugLog,
            forceFreshStart: forceFreshStart,
            ct: ct);

        if (newPort is null)
            throw new InvalidOperationException(
                $"QueryLens daemon unavailable for workspace '{_workspacePath}'.");

        _debugLog?.Invoke($"daemon-reconnect new-port={newPort.Value}");

        var candidate = await ConnectCandidateAsync(newPort.Value, ct);

        var oldEngine = _inner;
        _inner = candidate;
        await oldEngine.DisposeAsync();

        _debugLog?.Invoke("daemon-reconnect success");
    }

    private async Task<DaemonBackedEngine> ConnectCandidateAsync(int port, CancellationToken ct)
    {
        var candidate = new DaemonBackedEngine("127.0.0.1", port, _contextName);
        try
        {
            using var timeoutCts = new CancellationTokenSource(_connectTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            await candidate.PingAsync(linkedCts.Token);
            return candidate;
        }
        catch
        {
            await candidate.DisposeAsync();
            throw;
        }
    }

    private static bool IsDaemonTransportFailure(Exception ex)
    {
        if (ex is RpcException
            || ex is HttpRequestException
            || ex is IOException
            || ex is SocketException
            || ex is EndOfStreamException
            || ex is ObjectDisposedException)
        {
            return true;
        }

        if (ex is InvalidOperationException invalidOp
            && (invalidOp.Message.Contains("transport", StringComparison.OrdinalIgnoreCase)
                || invalidOp.Message.Contains("disconnected", StringComparison.OrdinalIgnoreCase)
                || invalidOp.InnerException is IOException))
        {
            return true;
        }

        return ex.InnerException is not null && IsDaemonTransportFailure(ex.InnerException);
    }
}
