using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EFQueryLens.VisualStudio.Host.Contracts;

internal sealed class SqlReadyHoverWatcherCore
{
    private const int PollIntervalMs = 200;

    private readonly ISqlReadyHoverPoller poller;
    private readonly ISqlReadyNotificationSink sink;
    private readonly Func<int> getNotificationWaitBudgetMs;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly Action<string> log;
    private readonly object sync = new();
    private readonly HashSet<string> activeWatches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> watchCancellation = new(
        StringComparer.OrdinalIgnoreCase
    );

    internal SqlReadyHoverWatcherCore(
        ISqlReadyHoverPoller poller,
        ISqlReadyNotificationSink sink,
        Func<int> getNotificationWaitBudgetMs,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        Action<string> log
    )
    {
        this.poller = poller;
        this.sink = sink;
        this.getNotificationWaitBudgetMs = getNotificationWaitBudgetMs;
        this.delayAsync = delayAsync;
        this.log = log;
    }

    internal int ActiveWatchCount
    {
        get
        {
            lock (sync)
            {
                return activeWatches.Count;
            }
        }
    }

    internal void Cancel(string filePath, int line, int character)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        string fileUri;
        try
        {
            fileUri = new Uri(filePath).AbsoluteUri;
        }
        catch
        {
            return;
        }

        var key = SqlReadyWatchKey.Build(fileUri, line, character);
        CancellationTokenSource? cts;
        lock (sync)
        {
            watchCancellation.TryGetValue(key, out cts);
        }

        if (cts is not null)
        {
            log($"sql-ready-watch-cancelled key={key} reason=inline-ready");
            cts.Cancel();
        }
    }

    internal void Watch(
        string filePath,
        int line,
        int character,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        string fileUri;
        try
        {
            fileUri = new Uri(filePath).AbsoluteUri;
        }
        catch
        {
            return;
        }

        var key = SqlReadyWatchKey.Build(fileUri, line, character);
        lock (sync)
        {
            if (!activeWatches.Add(key))
            {
                log($"sql-ready-watch-coalesced key={key}");
                return;
            }
        }

        log($"sql-ready-watch-started key={key}");
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (sync)
        {
            watchCancellation[key] = linkedCts;
        }

        _ = RunWatchAsync(filePath, line, character, key, linkedCts.Token);
    }

    internal Task WatchForTestsAsync(
        string filePath,
        int line,
        int character,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.CompletedTask;
        }

        string fileUri;
        try
        {
            fileUri = new Uri(filePath).AbsoluteUri;
        }
        catch
        {
            return Task.CompletedTask;
        }

        var key = SqlReadyWatchKey.Build(fileUri, line, character);
        lock (sync)
        {
            if (!activeWatches.Add(key))
            {
                return Task.CompletedTask;
            }
        }

        return RunWatchAsync(filePath, line, character, key, cancellationToken);
    }

    private async Task RunWatchAsync(
        string filePath,
        int line,
        int character,
        string key,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var waitBudgetMs = getNotificationWaitBudgetMs();
            var deadlineUtc = DateTime.UtcNow.AddMilliseconds(waitBudgetMs);
            var sawInQueue = true;

            while (!cancellationToken.IsCancellationRequested)
            {
                var response = await poller.PollAsync(filePath, line, character, cancellationToken);
                if (response is null)
                {
                    log($"sql-ready-watch-exit key={key} reason=null-response");
                    return;
                }

                if (QueryLensHostHoverPollResult.IsQueued(response.Status))
                {
                    sawInQueue = true;
                }
                else if (QueryLensHostHoverPollResult.IsTerminal(response.Status))
                {
                    if (
                        response.Status == QueryLensHostHoverPollStatus.Ready
                        && (!response.Success || response.CommandCount <= 0)
                    )
                    {
                        log(
                            $"sql-ready-watch-exit key={key} reason=terminal-not-ready "
                                + $"success={response.Success} commands={response.CommandCount}"
                        );
                        return;
                    }

                    if (
                        sawInQueue
                        && response.Status == QueryLensHostHoverPollStatus.Ready
                        && response.Success
                        && response.CommandCount > 0
                    )
                    {
                        log($"sql-ready-watch-ready key={key} commands={response.CommandCount}");
                        sink.RaiseFromWatch(filePath, line, character, response);
                    }

                    return;
                }
                else
                {
                    return;
                }

                if (DateTime.UtcNow >= deadlineUtc)
                {
                    log(
                        $"sql-ready-watch-timeout key={key} budgetMs={waitBudgetMs} status={response.Status}"
                    );
                    return;
                }

                await delayAsync(TimeSpan.FromMilliseconds(PollIntervalMs), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            log($"sql-ready-watch-cancelled key={key}");
        }
        catch (Exception ex)
        {
            log($"sql-ready-watch-failed key={key} type={ex.GetType().Name} message={ex.Message}");
        }
        finally
        {
            lock (sync)
            {
                activeWatches.Remove(key);
                watchCancellation.Remove(key);
            }
        }
    }
}
