// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using System;
using System.Threading;
using System.Threading.Tasks;
using EFQueryLens.VisualStudio.Host.Contracts;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

internal static class QueryLensSqlReadyStatusBarFlash
{
    internal const int FlashDurationMs = 5000;

    private static int generation;

    internal static void Show(string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (Package.GetGlobalService(typeof(SVsStatusbar)) is not IVsStatusbar statusBar)
        {
            QueryLensLanguageClient.LogSqlReadyDiagnostic("sql-ready-statusbar-flash-failed no-statusbar");
            return;
        }

        var flashGeneration = Interlocked.Increment(ref generation);
        statusBar.SetText(message);

        object animationIcon = (short)Constants.SBAI_General;
        statusBar.Animation(1, ref animationIcon);

        QueryLensLanguageClient.LogSqlReadyDiagnostic($"sql-ready-statusbar-flash generation={flashGeneration}");

        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await Task.Delay(FlashDurationMs).ConfigureAwait(true);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (!SqlReadyNotificationPresentationLogic.ShouldRestoreStatusBarFlash(generation, flashGeneration))
                {
                    QueryLensLanguageClient.LogSqlReadyDiagnostic(
                        $"sql-ready-statusbar-flash-restore-skipped generation={flashGeneration} current={generation}");
                    return;
                }

                statusBar.Animation(0, ref animationIcon);
                QueryLensStatusBarManager.SetStatusText(QueryLensStatusBarManager.CurrentText);
                QueryLensLanguageClient.LogSqlReadyDiagnostic(
                    $"sql-ready-statusbar-flash-restored generation={flashGeneration}");
            }
            catch (Exception ex)
            {
                QueryLensLanguageClient.LogSqlReadyDiagnostic(
                    $"sql-ready-statusbar-flash-failed type={ex.GetType().Name} message={ex.Message}");
            }
        });
    }
}
