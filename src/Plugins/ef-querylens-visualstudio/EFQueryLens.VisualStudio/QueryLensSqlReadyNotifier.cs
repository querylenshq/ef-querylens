// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using System;
using EFQueryLens.VisualStudio.Host.Contracts;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

internal static class QueryLensSqlReadyNotifier
{
    private const string ActionGoToQuery = "goToQuery";
    private const string ActionOpenSql = "openSql";

    private enum InfoBarHostKind
    {
        Document,
        SolutionExplorer,
    }

    private static string FormatHostKind(InfoBarHostKind hostKind)
        => hostKind switch
        {
            InfoBarHostKind.SolutionExplorer => "solution-explorer",
            _ => "document",
        };

    internal static void Show(QueryLensHostSqlReadyNotification notification, string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _ = TryShow(notification, message);
    }

    internal static bool TryShow(QueryLensHostSqlReadyNotification notification, string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        ShowLayeredCues(notification);

        if (TryShowInfoBar(notification, message, out var hostKind))
        {
            QueryLensLanguageClient.LogSqlReadyDiagnostic($"sql-ready-infobar-host={FormatHostKind(hostKind)}");
            return true;
        }

        return ShowShellFallback(message);
    }

    private static void ShowLayeredCues(QueryLensHostSqlReadyNotification notification)
    {
        var statusMessage = SqlReadyNotificationPresentationLogic.BuildStatusBarMessage(notification);
        QueryLensSqlReadyStatusBarFlash.Show(statusMessage);

        var outputLine = SqlReadyNotificationPresentationLogic.BuildOutputLine(notification);
        QueryLensLogOpener.WriteUserNotificationLine(outputLine);
        QueryLensLanguageClient.LogSqlReadyDiagnostic("sql-ready-output-line");
    }

    private static bool TryShowInfoBar(
        QueryLensHostSqlReadyNotification notification,
        string message,
        out InfoBarHostKind hostKind)
    {
        hostKind = InfoBarHostKind.Document;

        var host = ResolveInfoBarHost(out hostKind);
        if (host is null)
        {
            QueryLensLanguageClient.LogSqlReadyDiagnostic("sql-ready-infobar-failed no-info-bar-host");
            return false;
        }

        return TryAddInfoBarToHost(host, notification, message);
    }

    private static bool TryAddInfoBarToHost(
        IVsInfoBarHost host,
        QueryLensHostSqlReadyNotification notification,
        string message)
    {
        if (Package.GetGlobalService(typeof(SVsInfoBarUIFactory)) is not IVsInfoBarUIFactory factory)
        {
            QueryLensLanguageClient.LogSqlReadyDiagnostic("sql-ready-infobar-failed no-ui-factory");
            return false;
        }

        var goToQuery = new InfoBarButton(QueryLensHostSqlReadyNotificationLogic.GoToQueryActionTitle, ActionGoToQuery);
        var openSql = new InfoBarButton(QueryLensHostSqlReadyNotificationLogic.OpenSqlActionTitle, ActionOpenSql);
        var model = new InfoBarModel(
            new[] { new InfoBarTextSpan(message) },
            new InfoBarActionItem[] { goToQuery, openSql },
            KnownMonikers.StatusOK,
            isCloseButtonVisible: true);

        var element = factory.CreateInfoBar(model);
        if (element is null)
        {
            QueryLensLanguageClient.LogSqlReadyDiagnostic("sql-ready-infobar-failed create-element-null");
            return false;
        }

        var events = new SqlReadyInfoBarEvents(notification);
        if (element.Advise(events, out var cookie) != VSConstants.S_OK)
        {
            QueryLensLanguageClient.LogSqlReadyDiagnostic("sql-ready-infobar-failed advise-failed");
            return false;
        }

        events.Cookie = cookie;
        host.AddInfoBar(element);
        QueryLensLanguageClient.LogSqlReadyDiagnostic("sql-ready-infobar-shown");
        return true;
    }

    private static bool ShowShellFallback(string message)
    {
        QueryLensLanguageClient.LogSqlReadyDiagnostic("sql-ready-shell-fallback");

        if (QueryLensPackage.Instance is not { } package)
        {
            return false;
        }

        VsShellUtilities.ShowMessageBox(
            package,
            message,
            "EF QueryLens",
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

        return true;
    }

    private static IVsInfoBarHost? ResolveInfoBarHost(out InfoBarHostKind hostKind)
    {
        hostKind = InfoBarHostKind.Document;

        if (Package.GetGlobalService(typeof(SVsShellMonitorSelection)) is IVsMonitorSelection selection)
        {
            selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out object frameObj);
            if (frameObj is IVsWindowFrame activeFrame)
            {
                var activeHost = TryGetInfoBarHost(activeFrame);
                if (activeHost is not null)
                {
                    return activeHost;
                }
            }
        }

        if (Package.GetGlobalService(typeof(SVsUIShell)) is IVsUIShell shell)
        {
            shell.GetDocumentWindowEnum(out IEnumWindowFrames? enumFrames);
            if (enumFrames is not null)
            {
                var frames = new IVsWindowFrame[1];
                while (enumFrames.Next(1, frames, out uint fetched) == VSConstants.S_OK && fetched > 0)
                {
                    var host = TryGetInfoBarHost(frames[0]);
                    if (host is not null)
                    {
                        return host;
                    }
                }
            }

            if (shell.FindToolWindow(0, new Guid(ToolWindowGuids80.SolutionExplorer), out var solutionExplorerFrame) == VSConstants.S_OK
                && solutionExplorerFrame is not null)
            {
                var solutionExplorerHost = TryGetInfoBarHost(solutionExplorerFrame);
                if (solutionExplorerHost is not null)
                {
                    hostKind = InfoBarHostKind.SolutionExplorer;
                    return solutionExplorerHost;
                }
            }
        }

        return null;
    }

    private static IVsInfoBarHost? TryGetInfoBarHost(IVsWindowFrame frame)
    {
        if (frame.GetProperty((int)__VSFPROPID7.VSFPROPID_InfoBarHost, out object? hostObj) == VSConstants.S_OK
            && hostObj is IVsInfoBarHost host)
        {
            return host;
        }

        return null;
    }

    private sealed class SqlReadyInfoBarEvents(QueryLensHostSqlReadyNotification notification) : IVsInfoBarUIEvents
    {
        internal uint Cookie { get; set; }

        public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
        {
            var context = actionItem.ActionContext as string;
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                if (string.Equals(context, ActionGoToQuery, StringComparison.Ordinal))
                {
                    await QueryLensLanguageClient.NavigateToQuerySourceAsync(
                        notification.FileUri,
                        notification.Line,
                        notification.Character);
                    return;
                }

                if (string.Equals(context, ActionOpenSql, StringComparison.Ordinal))
                {
                    await QueryLensLanguageClient.OpenSqlAtQueryAsync(
                        notification.FileUri,
                        notification.Line,
                        notification.Character);
                }
            });

            infoBarUIElement.Unadvise(Cookie);
        }

        public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
        {
            infoBarUIElement.Unadvise(Cookie);
        }
    }
}
