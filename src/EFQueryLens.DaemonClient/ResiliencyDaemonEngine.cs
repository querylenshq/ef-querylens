using System.IO.Pipes;
using EFQueryLens.Core;
using StreamJsonRpc;

namespace EFQueryLens.DaemonClient;

/// <summary>
/// Wraps <see cref="DaemonBackedEngine"/> with transparent reconnection logic.
/// If the daemon transport fails, re-discover/restart daemon and retry once.
/// </summary>
public sealed class ResiliencyDaemonEngine : IQueryLensEngine, IAsyncDisposable
{
    private DaemonBackedEngine _inner;
    private readonly string _workspacePath;
    private readonly string? _daemonExecutablePath;
    private readonly string? _daemonAssemblyPath;
    private readonly string _contextName;
    private readonly int _connectTimeoutMs;
    private readonly int _startTimeoutMs;
    private readonly Action<string>? _debugLog;
    private readonly SemaphoreSlim _reconnectGate = new(1, 1);
    private volatile bool _disposed;

    public ResiliencyDaemonEngine(
        DaemonBackedEngine inner,
        string workspacePath,
        string? daemonExecutablePath,
        string? daemonAssemblyPath,
        string contextName,
        int connectTimeoutMs = 2500,
        int startTimeoutMs = 10000,
        Action<string>? debugLog = null)
    {
        _inner = inner;
        _workspacePath = workspacePath;
        _daemonExecutablePath = daemonExecutablePath;
        _daemonAssemblyPath = daemonAssemblyPath;
        _contextName = contextName;
        _connectTimeoutMs = connectTimeoutMs;
        _startTimeoutMs = startTimeoutMs;
        _debugLog = debugLog;
    }

    public async Task<QueryTranslationResult> TranslateAsync(
        TranslationRequest request, CancellationToken ct = default) =>
        await ExecuteWithReconnectAsync(e => e.TranslateAsync(request, ct), ct);

    public Task<ExplainResult> ExplainAsync(
        ExplainRequest request, CancellationToken ct = default) =>
        _inner.ExplainAsync(request, ct);

    public async Task<ModelSnapshot> InspectModelAsync(
        ModelInspectionRequest request, CancellationToken ct = default) =>
        await ExecuteWithReconnectAsync(e => e.InspectModelAsync(request, ct), ct);

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _inner.DisposeAsync();
        _reconnectGate.Dispose();
    }

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
            _debugLog?.Invoke($"daemon-reconnect workspace={_workspacePath}");

            var newPipeName = await DaemonLocator.TryGetOrStartDaemonAsync(
                _workspacePath,
                _daemonExecutablePath,
                _daemonAssemblyPath,
                timeoutMilliseconds: _startTimeoutMs,
                debugLog: _debugLog,
                ct: ct);

            if (string.IsNullOrWhiteSpace(newPipeName))
                throw new InvalidOperationException(
                    $"QueryLens daemon unavailable for workspace '{_workspacePath}'.");

            _debugLog?.Invoke($"daemon-reconnect new-pipe={newPipeName}");

            var pipe = new NamedPipeClientStream(
                ".",
                newPipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            try
            {
                using var timeoutCts = new CancellationTokenSource(_connectTimeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                await pipe.ConnectAsync(linkedCts.Token);
            }
            catch
            {
                await pipe.DisposeAsync();
                throw;
            }

            var oldEngine = _inner;
            _inner = new DaemonBackedEngine(pipe, _contextName);
            await oldEngine.DisposeAsync();

            _debugLog?.Invoke("daemon-reconnect success");
        }
        finally
        {
            _reconnectGate.Release();
        }
    }

    private static bool IsDaemonTransportFailure(Exception ex)
    {
        if (ex is ConnectionLostException
            || ex is IOException
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
