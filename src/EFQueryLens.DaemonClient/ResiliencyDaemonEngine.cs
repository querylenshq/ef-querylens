using EFQueryLens.Core;
using EFQueryLens.Core.Grpc;

namespace EFQueryLens.DaemonClient;

/// <summary>
/// Wraps <see cref="DaemonBackedEngine"/> with transparent reconnection logic.
/// If the daemon transport fails, re-discover/restart daemon and retry once.
/// </summary>
public sealed partial class ResiliencyDaemonEngine : IQueryLensEngine, IAsyncDisposable
{
    private DaemonBackedEngine _inner;
    private readonly string _workspacePath;
    private readonly string? _daemonExecutablePath;
    private readonly string? _daemonAssemblyPath;
    private readonly string _contextName;
    private readonly int _connectTimeoutMs;
    private readonly int _startTimeoutMs;
    private readonly Action<string>? _debugLog;
    private readonly bool _shutdownDaemonOnDispose;
    private bool _ownsDaemonLifecycle;
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
        bool shutdownDaemonOnDispose = false,
        bool ownsDaemonLifecycle = false,
        Action<string>? debugLog = null)
    {
        _inner = inner;
        _workspacePath = workspacePath;
        _daemonExecutablePath = daemonExecutablePath;
        _daemonAssemblyPath = daemonAssemblyPath;
        _contextName = contextName;
        _connectTimeoutMs = connectTimeoutMs;
        _startTimeoutMs = startTimeoutMs;
        _shutdownDaemonOnDispose = shutdownDaemonOnDispose;
        _ownsDaemonLifecycle = ownsDaemonLifecycle;
        _debugLog = debugLog;
    }

    public async Task<QueryTranslationResult> TranslateAsync(
        TranslationRequest request, CancellationToken ct = default) =>
        await ExecuteWithReconnectAsync(e => e.TranslateAsync(request, ct), ct);

    public async Task<QueuedTranslationResult> TranslateQueuedAsync(
        TranslationRequest request,
        CancellationToken ct = default) =>
        await ExecuteWithReconnectAsync(e => e.TranslateQueuedAsync(request, ct), ct);

    public Task<ExplainResult> ExplainAsync(
        ExplainRequest request, CancellationToken ct = default) =>
        _inner.ExplainAsync(request, ct);

    public async Task<EFQueryLens.Core.ModelSnapshot> InspectModelAsync(
        ModelInspectionRequest request, CancellationToken ct = default) =>
        await ExecuteWithReconnectAsync(e => e.InspectModelAsync(request, ct), ct);

    public async ValueTask DisposeAsync() => await DisposeCoreAsync();

    /// <summary>
    /// Requests daemon restart by shutting down current daemon and reconnecting.
    /// </summary>
    public async Task<bool> RestartDaemonAsync(CancellationToken ct = default) =>
        await RestartDaemonCoreAsync(ct);

    public async Task<InvalidateCacheResponse> InvalidateQueryCachesAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await ExecuteWithReconnectAsync(e => e.InvalidateQueryCachesAsync(ct), ct);
    }

    /// <summary>
    /// Runs a resilient daemon event subscription loop. If the stream drops due to
    /// transport issues, this method reconnects and re-subscribes until cancelled.
    /// </summary>
    public async Task RunDaemonEventSubscriptionAsync(
        Action<DaemonEvent> onEvent,
        CancellationToken ct = default) =>
        await RunDaemonEventSubscriptionCoreAsync(onEvent, ct);
}
