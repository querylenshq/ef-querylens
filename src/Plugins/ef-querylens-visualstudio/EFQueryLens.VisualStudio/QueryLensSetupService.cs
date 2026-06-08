// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

internal static class QueryLensSetupService
{
    private static int _setupInProgress;

    internal static void RunFromActiveEditor(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!QueryLensEditorContext.TryGetActiveCSharpContext(out QueryLensActiveEditorContext? context, out string? errorMessage)
            || context is null)
        {
            VsShellUtilities.ShowMessageBox(
                package,
                errorMessage ?? "Open the C# file with the EF query, then run Set up QueryLens.",
                "EF QueryLens",
                OLEMSGICON.OLEMSGICON_WARNING,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            return;
        }

        Run(package, context);
    }

    internal static void Run(AsyncPackage package, QueryLensActiveEditorContext context)
    {
        if (Interlocked.CompareExchange(ref _setupInProgress, 1, 0) != 0)
        {
            VsShellUtilities.ShowMessageBox(
                package,
                "EF QueryLens: setup is already running.",
                "EF QueryLens",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            return;
        }

        JoinableTask setupTask = package.JoinableTaskFactory.RunAsync(async delegate
        {
            try
            {
                await RunCoreAsync(package, context, CancellationToken.None);
            }
            finally
            {
                Interlocked.Exchange(ref _setupInProgress, 0);
            }
        });

        setupTask.FileAndForget("efquerylens/QueryLensSetupService/Run");
    }

    private static async Task RunCoreAsync(
        AsyncPackage package,
        QueryLensActiveEditorContext context,
        CancellationToken cancellationToken)
    {
        var detectResponse = await QueryLensLanguageClient.RequestSetupDetectAsync(
            context.DocumentUri,
            context.Line,
            context.Character,
            cancellationToken);

        if (!detectResponse.Success || detectResponse.Result is null)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            VsShellUtilities.ShowMessageBox(
                package,
                detectResponse.Message,
                "EF QueryLens",
                OLEMSGICON.OLEMSGICON_WARNING,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            return;
        }

        QueryLensLanguageClient.SetupDetectResult? detect = detectResponse.Result;

        if (detect == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(detect.Message) && detect.Hosts.Count == 0)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            VsShellUtilities.ShowMessageBox(package,
                                            message: detect.Message!,
                                            title: "EF QueryLens",
                                            icon: OLEMSGICON.OLEMSGICON_WARNING,
                                            msgButton: OLEMSGBUTTON.OLEMSGBUTTON_OK,
                                            defaultButton: OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            return;
        }

        var hostProjectPath = detect.DefaultHostProjectPath;
        if (detect.RequiresHostSelection)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            hostProjectPath = QueryLensSetupDialogs.PickHost(detect.Hosts);
            if (string.IsNullOrWhiteSpace(hostProjectPath))
            {
                return;
            }
        }

        await RunApplyAsync(package, context, hostProjectPath, provider: null, cancellationToken);
    }

    private static async Task RunApplyAsync(
        AsyncPackage package,
        QueryLensActiveEditorContext context,
        string? hostProjectPath,
        string? provider,
        CancellationToken cancellationToken)
    {
        var applyResponse = await QueryLensLanguageClient.RequestSetupApplyAsync(
            context.DocumentUri,
            hostProjectPath,
            provider,
            force: false,
            cancellationToken);

        if (!applyResponse.Success || applyResponse.Result is null)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            VsShellUtilities.ShowMessageBox(
                package,
                applyResponse.Message,
                "EF QueryLens",
                OLEMSGICON.OLEMSGICON_WARNING,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            return;
        }

        var apply = applyResponse.Result;
        if (string.Equals(apply.Action, "NeedProvider", StringComparison.OrdinalIgnoreCase))
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var selectedProvider = QueryLensSetupDialogs.PickProvider();
            if (string.IsNullOrWhiteSpace(selectedProvider))
            {
                return;
            }

            await RunApplyAsync(package, context, hostProjectPath, selectedProvider, cancellationToken);
            return;
        }

        await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        if (!apply.Success)
        {
            VsShellUtilities.ShowMessageBox(
                package,
                apply.Message,
                "EF QueryLens",
                OLEMSGICON.OLEMSGICON_WARNING,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            return;
        }

        var openedFactory = TryOpenGeneratedFactory(apply.GeneratedFilePath);
        var message = openedFactory
            ? BuildFactoryOpenedMessage(apply.RequiresReview)
            : apply.Message;
        var icon = apply.RequiresReview
            ? OLEMSGICON.OLEMSGICON_WARNING
            : OLEMSGICON.OLEMSGICON_INFO;
        VsShellUtilities.ShowMessageBox(
            package,
            message,
            "EF QueryLens",
            icon,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    private static bool TryOpenGeneratedFactory(string? generatedFilePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (string.IsNullOrWhiteSpace(generatedFilePath) || !File.Exists(generatedFilePath))
        {
            return false;
        }

        try
        {
            if (Package.GetGlobalService(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte)
            {
                dte.ItemOperations.OpenFile(generatedFilePath);
                return true;
            }
        }
        catch
        {
            // Fall through to message-box fallback.
        }

        return false;
    }

    private static string BuildFactoryOpenedMessage(bool requiresReview)
    {
        const string message =
            "Factory opened — rebuild the project, then confirm each CreateOfflineContext().";

        return requiresReview
            ? message + " Review best-effort defaults if any DbContext did not match AddDbContext."
            : message;
    }
}
