using EFQueryLens.Lsp.Services;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class QueryLensStatusTrackerTests
{
    [Fact]
    public void GetSnapshot_InitiallyStarting()
    {
        var tracker = new QueryLensStatusTracker();

        var snapshot = tracker.GetSnapshot();

        Assert.Equal(QueryLensHostState.Starting, snapshot.State);
    }

    [Fact]
    public void SetDaemonReady_BeforeAssemblyWarmup_ShowsWarming()
    {
        var tracker = new QueryLensStatusTracker();

        tracker.SetDaemonReady(ready: true);
        var snapshot = tracker.GetSnapshot();

        Assert.Equal(QueryLensHostState.Warming, snapshot.State);
        Assert.Contains("first query", snapshot.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BeginCompute_TransitionsToComputing()
    {
        var tracker = new QueryLensStatusTracker();
        tracker.SetDaemonReady(ready: true);

        using (tracker.BeginCompute())
        {
            var snapshot = tracker.GetSnapshot();
            Assert.Equal(QueryLensHostState.Computing, snapshot.State);
            Assert.Equal(1, snapshot.InflightCount);
        }

        Assert.Equal(QueryLensHostState.Warming, tracker.GetSnapshot().State);
    }

    [Fact]
    public void BeginWarmup_TransitionsToWarming()
    {
        var tracker = new QueryLensStatusTracker();
        tracker.SetDaemonReady(ready: true);

        using (tracker.BeginWarmup())
        {
            var snapshot = tracker.GetSnapshot();
            Assert.Equal(QueryLensHostState.Warming, snapshot.State);
        }

        Assert.Equal(QueryLensHostState.Warming, tracker.GetSnapshot().State);
    }

    [Fact]
    public void SetDaemonReadyFalse_TransitionsToUnavailable()
    {
        var tracker = new QueryLensStatusTracker();
        tracker.SetDaemonReady(ready: true);
        tracker.SetDaemonReady(ready: false);

        var snapshot = tracker.GetSnapshot();

        Assert.Equal(QueryLensHostState.Unavailable, snapshot.State);
    }

    [Fact]
    public void SetAssemblyWarmed_MarksSnapshotWarmed()
    {
        var tracker = new QueryLensStatusTracker();
        tracker.SetDaemonReady(ready: true, assemblyPath: @"C:\app\bin\Debug\net8.0\App.dll");
        tracker.SetAssemblyWarmed(warmed: true, assemblyPath: @"C:\app\bin\Debug\net8.0\App.dll");

        var snapshot = tracker.GetSnapshot();

        Assert.True(snapshot.Warmed);
        Assert.Equal(QueryLensHostState.Ready, snapshot.State);
    }
}
