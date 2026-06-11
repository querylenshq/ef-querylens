using System.Collections.Generic;
using EFQueryLens.VisualStudio.Host.Contracts;

namespace EFQueryLens.Core.Tests.VisualStudio;

public sealed class SqlReadyHoverWatcherCoreTests
{
    [Fact]
    public async Task NotifiesAfterInQueueThenReady()
    {
        var sequence = new Queue<QueryLensHostHoverPollResult?>([
            Queued(),
            Queued(),
            Ready(),
        ]);
        var sink = new CapturingSink();
        var core = CreateCore(sequence, sink, waitBudgetMs: 5_000);

        await core.WatchForTestsAsync(@"C:\proj\a.cs", 1, 2);

        Assert.Single(sink.Raised);
        Assert.Equal(1, sink.Raised[0].Line);
    }

    [Fact]
    public async Task DoesNotNotifyInstantReady()
    {
        var sequence = new Queue<QueryLensHostHoverPollResult?>([Ready()]);
        var sink = new CapturingSink();
        var core = CreateCore(sequence, sink, waitBudgetMs: 5_000);

        await core.WatchForTestsAsync(@"C:\proj\a.cs", 1, 2);

        Assert.Empty(sink.Raised);
    }

    [Fact]
    public async Task TimesOutWhileStillInQueue()
    {
        var sequence = new Queue<QueryLensHostHoverPollResult?>(Enumerable.Repeat(Queued(), 100).Cast<QueryLensHostHoverPollResult?>().ToList());
        var sink = new CapturingSink();
        var logs = new List<string>();
        var core = CreateCore(sequence, sink, waitBudgetMs: 500, logs.Add);

        await core.WatchForTestsAsync(@"C:\proj\a.cs", 1, 2);

        Assert.Empty(sink.Raised);
        Assert.Contains(logs, line => line.Contains("sql-ready-watch-timeout", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExitsImmediatelyOnNullResponse()
    {
        var sequence = new Queue<QueryLensHostHoverPollResult?>([null]);
        var sink = new CapturingSink();
        var logs = new List<string>();
        var core = CreateCore(sequence, sink, waitBudgetMs: 60_000, logs.Add);

        await core.WatchForTestsAsync(@"C:\proj\a.cs", 1, 2);

        Assert.Empty(sink.Raised);
        Assert.Contains(logs, line => line.Contains("null-response", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, line => line.Contains("sql-ready-watch-timeout", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExitsOnTerminalFailedReadyWithoutTimeout()
    {
        var sequence = new Queue<QueryLensHostHoverPollResult?>([
            Queued(),
            new()
            {
                Status = QueryLensHostHoverPollStatus.Ready,
                Success = false,
                CommandCount = 0,
            },
        ]);
        var sink = new CapturingSink();
        var logs = new List<string>();
        var core = CreateCore(sequence, sink, waitBudgetMs: 60_000, logs.Add);

        await core.WatchForTestsAsync(@"C:\proj\a.cs", 1, 2);

        Assert.Empty(sink.Raised);
        Assert.Contains(logs, line => line.Contains("terminal-not-ready", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, line => line.Contains("sql-ready-watch-timeout", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CoalescesDuplicateWatch()
    {
        var sequence = new Queue<QueryLensHostHoverPollResult?>(Enumerable.Repeat(Queued(), 50).Cast<QueryLensHostHoverPollResult?>().ToList());
        var sink = new CapturingSink();
        var core = CreateCore(sequence, sink, waitBudgetMs: 1_000);

        core.Watch(@"C:\proj\a.cs", 1, 2);
        await core.WatchForTestsAsync(@"C:\proj\a.cs", 1, 2);

        Assert.Empty(sink.Raised);
    }

    private static SqlReadyHoverWatcherCore CreateCore(
        Queue<QueryLensHostHoverPollResult?> sequence,
        CapturingSink sink,
        int waitBudgetMs,
        Action<string>? log = null)
    {
        return new SqlReadyHoverWatcherCore(
            new ScriptPoller(sequence),
            sink,
            () => waitBudgetMs,
            static (_, _) => Task.CompletedTask,
            log ?? (_ => { }));
    }

    private static QueryLensHostHoverPollResult Queued() => new() { Status = QueryLensHostHoverPollStatus.InQueue };

    private static QueryLensHostHoverPollResult Ready() =>
        new() { Status = QueryLensHostHoverPollStatus.Ready, Success = true, CommandCount = 1 };

    private sealed class ScriptPoller(Queue<QueryLensHostHoverPollResult?> sequence) : ISqlReadyHoverPoller
    {
        public Task<QueryLensHostHoverPollResult?> PollAsync(
            string filePath,
            int line,
            int character,
            CancellationToken cancellationToken)
        {
            if (sequence.Count == 0)
            {
                return Task.FromResult<QueryLensHostHoverPollResult?>(Queued());
            }

            return Task.FromResult(sequence.Dequeue());
        }
    }

    private sealed class CapturingSink : ISqlReadyNotificationSink
    {
        public List<QueryLensHostSqlReadyNotification> Raised { get; } = [];

        public void RaiseFromWatch(string filePath, int line, int character, QueryLensHostHoverPollResult response)
        {
            Raised.Add(new QueryLensHostSqlReadyNotification(
                new Uri(filePath).AbsoluteUri,
                line,
                character,
                Path.GetFileName(filePath),
                response.CommandCount));
        }
    }
}
