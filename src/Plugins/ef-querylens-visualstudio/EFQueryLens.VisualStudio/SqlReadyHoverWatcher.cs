// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using System;
using System.Threading;
using System.Threading.Tasks;
using EFQueryLens.VisualStudio.Host.Contracts;
using Microsoft.VisualStudio.Shell;

internal static class SqlReadyHoverWatcher
{
    private static readonly SqlReadyHoverWatcherCore Core = new(
        new LanguageClientHoverPoller(),
        new LanguageClientNotificationSink(),
        () => SqlReadyWatchBudget.ComputeNotificationWaitMs(QueryLensOptionsPage.Current?.HoverWaitWhenWarmMs ?? 8000),
        static (delay, cancellationToken) => Task.Delay(delay, cancellationToken),
        LogWatchEvent);

    internal static void WatchIfQueued(string filePath, int line, int character, int status)
    {
        if (!QueryLensHostHoverPollResult.IsQueued(status))
        {
            return;
        }

        var options = QueryLensOptionsPage.Current;
        if (options?.NotifyWhenSqlReady == false)
        {
            return;
        }

        Core.Watch(filePath, line, character);
    }

    private sealed class LanguageClientHoverPoller : ISqlReadyHoverPoller
    {
        public async Task<QueryLensHostHoverPollResult?> PollAsync(
            string filePath,
            int line,
            int character,
            CancellationToken cancellationToken)
        {
            var response = await QueryLensLanguageClient.TryGetStructuredHoverAsync(
                filePath,
                line,
                character,
                cancellationToken,
                refreshStatus: false,
                startSqlReadyWatch: false);

            if (response is null)
            {
                return null;
            }

            return new QueryLensHostHoverPollResult
            {
                Success = response.Success,
                CommandCount = response.CommandCount,
                Status = response.Status,
            };
        }
    }

    private static void LogWatchEvent(string message)
    {
        if (message.IndexOf("sql-ready-watch-coalesced", StringComparison.Ordinal) >= 0
            || message.IndexOf("sql-ready-watch-exit", StringComparison.Ordinal) >= 0)
        {
            QueryLensLogOpener.WriteClientDiagnosticLine(message);
            return;
        }

        QueryLensLanguageClient.LogSqlReadyDiagnostic(message);
    }

    private sealed class LanguageClientNotificationSink : ISqlReadyNotificationSink
    {
        public void RaiseFromWatch(string filePath, int line, int character, QueryLensHostHoverPollResult response)
        {
            QueryLensLanguageClient.TryRaiseSqlReadyFromHover(
                filePath,
                line,
                character,
                new QueryLensStructuredHoverResponse
                {
                    Success = response.Success,
                    CommandCount = response.CommandCount,
                    Status = response.Status,
                });
        }
    }
}
