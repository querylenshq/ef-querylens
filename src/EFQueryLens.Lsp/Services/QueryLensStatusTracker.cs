using System.Collections.Concurrent;
using StreamJsonRpc;

using EFQueryLens.Lsp.Protocol;

namespace EFQueryLens.Lsp.Services;

/// <summary>
/// Tracks global QueryLens readiness and pushes <c>efquerylens/statusChanged</c> to clients.
/// </summary>
internal sealed class QueryLensStatusTracker
{
    private readonly object _gate = new();
    private QueryLensStatusSnapshot _snapshot = new(QueryLensHostState.Starting, "Starting QueryLens…");
    private int _inflightComputes;
    private int _inflightWarmups;
    private int _inflightPrewarms;
    private bool _daemonReady;
    private bool _daemonConfigured;
    private bool _assemblyWarmed;
    private string? _assemblyPath;
    private JsonRpc? _rpc;

    public void AttachRpc(JsonRpc? rpc)
    {
        _rpc = rpc;
        PublishIfChanged(force: true);
    }

    public void SetDaemonReady(bool ready, string? assemblyPath = null)
    {
        lock (_gate)
        {
            _daemonConfigured = true;
            _daemonReady = ready;
            if (!string.IsNullOrWhiteSpace(assemblyPath))
            {
                _assemblyPath = assemblyPath;
            }
        }

        PublishIfChanged();
    }

    public void SetAssemblyWarmed(bool warmed, string? assemblyPath = null)
    {
        lock (_gate)
        {
            _assemblyWarmed = warmed;
            if (!string.IsNullOrWhiteSpace(assemblyPath))
            {
                _assemblyPath = assemblyPath;
            }
        }

        PublishIfChanged();
    }

    public IDisposable BeginCompute() => BeginWork(
        () => { lock (_gate) _inflightComputes++; },
        () => { lock (_gate) if (_inflightComputes > 0) _inflightComputes--; });

    public IDisposable BeginWarmup() => BeginWork(
        () => { lock (_gate) _inflightWarmups++; },
        () => { lock (_gate) if (_inflightWarmups > 0) _inflightWarmups--; });

    public IDisposable BeginPrewarm() => BeginWork(
        () => { lock (_gate) _inflightPrewarms++; },
        () => { lock (_gate) if (_inflightPrewarms > 0) _inflightPrewarms--; });

    public QueryLensStatusSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return BuildSnapshotLocked();
        }
    }

    private IDisposable BeginWork(Action increment, Action decrement)
    {
        increment();
        PublishIfChanged();

        return new WorkScope(() =>
        {
            decrement();
            PublishIfChanged();
        });
    }

    private void PublishIfChanged(bool force = false)
    {
        QueryLensStatusSnapshot next;
        QueryLensStatusSnapshot previous;

        lock (_gate)
        {
            previous = _snapshot;
            next = BuildSnapshotLocked();
            if (!force && SnapshotEquals(previous, next))
            {
                return;
            }

            _snapshot = next;
        }

        _ = NotifyStatusChangedAsync(next);
    }

    private QueryLensStatusSnapshot BuildSnapshotLocked()
    {
        if (!_daemonConfigured)
        {
            return new QueryLensStatusSnapshot(
                QueryLensHostState.Starting,
                "Starting QueryLens…",
                _assemblyPath,
                _inflightComputes,
                _assemblyWarmed);
        }

        if (!_daemonReady)
        {
            return new QueryLensStatusSnapshot(
                QueryLensHostState.Unavailable,
                "QueryLens engine is unavailable.",
                _assemblyPath,
                _inflightComputes,
                _assemblyWarmed);
        }

        if (_inflightComputes > 0)
        {
            return new QueryLensStatusSnapshot(
                QueryLensHostState.Computing,
                "Translating LINQ to SQL…",
                _assemblyPath,
                _inflightComputes,
                _assemblyWarmed);
        }

        if (_inflightWarmups > 0 || _inflightPrewarms > 0)
        {
            return WarmingSnapshot("Warming translation services…");
        }

        if (_assemblyWarmed)
        {
            return new QueryLensStatusSnapshot(
                QueryLensHostState.Ready,
                "Ready",
                _assemblyPath,
                _inflightComputes,
                warmed: true);
        }

        // Daemon is up but the target assembly has not been inspected yet — first hover
        // or warmup can take a long time while shadow bundles load.
        return WarmingSnapshot("Warming — first query may take longer…");

        QueryLensStatusSnapshot WarmingSnapshot(string message) =>
            new(
                QueryLensHostState.Warming,
                message,
                _assemblyPath,
                _inflightComputes,
                _assemblyWarmed);
    }

    private static bool SnapshotEquals(QueryLensStatusSnapshot a, QueryLensStatusSnapshot b) =>
        a.State == b.State
        && a.Message == b.Message
        && string.Equals(a.AssemblyPath, b.AssemblyPath, StringComparison.OrdinalIgnoreCase)
        && a.InflightCount == b.InflightCount
        && a.Warmed == b.Warmed;

    private async Task NotifyStatusChangedAsync(QueryLensStatusSnapshot snapshot)
    {
        var rpc = _rpc;
        if (rpc is null)
        {
            return;
        }

        try
        {
            await rpc.NotifyAsync(LspProtocolMethods.StatusChangedNotification, snapshot);
        }
        catch
        {
            // Best-effort — client may have disconnected.
        }
    }

    private sealed class WorkScope(Action onDispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            onDispose();
        }
    }
}
