namespace EFQueryLens.DaemonClient;

/// <summary>
/// Obsolete. Use <see cref="DaemonBackedEngine"/> directly with a connected
/// <see cref="System.IO.Pipes.NamedPipeClientStream"/>.
/// </summary>
[Obsolete("DaemonConnection is obsolete. Construct DaemonBackedEngine with a NamedPipeClientStream directly.")]
public sealed class DaemonConnection : IAsyncDisposable
{
    public DaemonConnection(string pipeName) { }
    public bool IsConnected => false;
    public Task ConnectAsync(CancellationToken ct, int timeoutMilliseconds = 5000) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}