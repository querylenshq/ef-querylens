// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using System;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using EFQueryLens.VisualStudio.Host.Contracts;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("EF QueryLens", "Preview EF Core LINQ SQL in Visual Studio", "0.0.1")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(QueryLensOptionsPage), "EF QueryLens", "General", 0, 0, true)]
[Guid(QueryLensCommandGuids.PackageString)]
internal sealed class QueryLensPackage : AsyncPackage
{
    internal static QueryLensPackage? Instance { get; private set; }

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        Instance = this;
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        if (await GetServiceAsync(typeof(IMenuCommandService)) is not OleMenuCommandService menuCommandService)
        {
            return;
        }

        await QueryLensLogOpener.InitializeOutputPaneAsync(this, cancellationToken);
        QueryLensStatusBarManager.Reset();

        AddMenuCommand(menuCommandService, QueryLensCommandIds.RestartDaemon, HandleRestartDaemonCommand);
        AddMenuCommand(menuCommandService, QueryLensCommandIds.OpenLogs, HandleOpenLogsCommand);
        AddMenuCommand(menuCommandService, QueryLensCommandIds.SetupQueryLens, HandleSetupQueryLensCommand);
        AddMenuCommand(menuCommandService, QueryLensCommandIds.TestSqlReadyNotify, HandleTestSqlReadyNotifyCommand);
    }

    private static void AddMenuCommand(OleMenuCommandService menuCommandService, int commandId, EventHandler handler)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var menuCommandId = new CommandID(new Guid(QueryLensCommandGuids.CommandSetString), commandId);
        var menuCommand = new OleMenuCommand(handler, menuCommandId);
        menuCommandService.AddCommand(menuCommand);
    }

    private void HandleRestartDaemonCommand(object sender, EventArgs e)
    {
        RunCommand(async cancellationToken =>
        {
            var result = await QueryLensLanguageClient.RequestDaemonRestartAsync(cancellationToken);

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var icon = result.Success ? OLEMSGICON.OLEMSGICON_INFO : OLEMSGICON.OLEMSGICON_WARNING;
            VsShellUtilities.ShowMessageBox(
                this,
                result.Success ? result.Message : $"[{result.Code}] {result.Message}",
                "EF QueryLens",
                icon,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        });
    }

    private void HandleSetupQueryLensCommand(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        QueryLensSetupService.RunFromActiveEditor(this);
    }

    private void HandleTestSqlReadyNotifyCommand(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string? filePath = null;
        var line = 0;
        var character = 0;

        if (QueryLensEditorContext.TryGetActiveCSharpContext(out var context, out _))
        {
            if (Uri.TryCreate(context!.DocumentUri, UriKind.Absolute, out var uri))
            {
                filePath = uri.LocalPath;
                line = context.Line;
                character = context.Character;
            }
        }

        var notification = SqlReadyTestNotificationBuilder.Build(filePath, line, character);
        var shown = QueryLensLanguageClient.ShowTestSqlReadyNotification(notification);

        if (!shown)
        {
            VsShellUtilities.ShowMessageBox(
                this,
                "EF QueryLens could not show the SQL-ready notification (InfoBar host and shell fallback both failed). " +
                $"Check {Path.Combine(Path.GetTempPath(), "EFQueryLens.VisualStudio.log")} for sql-ready-test-menu-* lines.",
                "EF QueryLens",
                OLEMSGICON.OLEMSGICON_CRITICAL,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }

    private void HandleOpenLogsCommand(object sender, EventArgs e)
    {
        RunCommand(async cancellationToken =>
        {
            var (success, message) = await QueryLensLogOpener.StartTailInOutputWindowAsync(this, cancellationToken);

            if (success)
            {
                return;
            }

            await JoinableTaskFactory.SwitchToMainThreadAsync();
            VsShellUtilities.ShowMessageBox(
                this,
                message,
                "EF QueryLens",
                OLEMSGICON.OLEMSGICON_WARNING,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }

            QueryLensLogOpener.StopTail();
            QueryLensLanguageClient.DisposeCurrent();
        }

        base.Dispose(disposing);
    }

    private void RunCommand(Func<CancellationToken, Task> action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        var commandTask = JoinableTaskFactory.RunAsync(async delegate
        {
            try
            {
                await action(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
            catch (Exception ex)
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                VsShellUtilities.ShowMessageBox(
                    this,
                    $"[{QueryLensErrorCodes.CommandExecutionFailed}] EF QueryLens command failed: {ex.Message}",
                    "EF QueryLens",
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        });

        commandTask.FileAndForget("efquerylens/QueryLensPackage/RunCommand");
    }
}
