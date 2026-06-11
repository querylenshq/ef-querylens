// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EFQueryLens.VisualStudio.Host.Contracts;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

internal sealed partial class QueryLensLanguageClient
{
    private static readonly QueryLensLspNotificationTarget NotificationTarget = new();

    internal static void HandleSqlReadyNotification(QueryLensHostSqlReadyNotification notification)
    {
        LogSqlReadyDiagnostic(
            $"sql-ready-received file={notification.FileName} line={notification.Line} " +
            $"char={notification.Character} commands={notification.CommandCount} uri={notification.FileUri}");

        var options = QueryLensOptionsPage.Current;
        var enabled = options?.NotifyWhenSqlReady ?? true;
        if (!enabled)
        {
            LogSqlReadyDiagnostic("sql-ready-suppressed setting-disabled");
            return;
        }

        if (!SqlReadyNotificationHandlerLogic.TryPrepareShow(
                notification,
                enabled,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                out var message))
        {
            LogSqlReadyDiagnostic(
                $"sql-ready-suppressed shouldShow=false fileUri={notification.FileUri} " +
                $"commandCount={notification.CommandCount}");
            return;
        }

        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            LogSqlReadyDiagnostic($"sql-ready-showing message={message}");
            QueryLensSqlReadyNotifier.Show(notification, message);
        });
    }

    internal static bool ShowTestSqlReadyNotification(QueryLensHostSqlReadyNotification notification)
    {
        QueryLensHostSqlReadyNotificationLogic.ResetDedupeForTests();
        LogSqlReadyDiagnostic(
            $"sql-ready-test-menu-invoked file={notification.FileName} line={notification.Line} char={notification.Character}");

        var message = QueryLensHostSqlReadyNotificationLogic.BuildToastMessage(notification);
        var shown = QueryLensSqlReadyNotifier.TryShow(notification, message);
        LogSqlReadyDiagnostic($"sql-ready-test-menu-result shown={shown}");
        return shown;
    }

    internal static void TryRaiseSqlReadyFromHover(
        string filePath,
        int line,
        int character,
        QueryLensStructuredHoverResponse response)
    {
        if (!response.Success || response.CommandCount <= 0)
        {
            return;
        }

        var fileUri = new Uri(filePath).AbsoluteUri;
        var fileName = Path.GetFileName(filePath);
        var notification = new QueryLensHostSqlReadyNotification(
            fileUri,
            line,
            character,
            fileName,
            response.CommandCount);

        LogSqlReadyDiagnostic(
            $"sql-ready-client-hover file={fileName} line={line} char={character} commands={response.CommandCount}");
        HandleSqlReadyNotification(notification);
    }

    internal static void LogSqlReadyDiagnostic(string message)
    {
        Log(message);
        QueryLensLogOpener.WriteClientDiagnosticLine(message);
    }

    internal static async Task NavigateToQuerySourceAsync(string fileUri, int line, int character)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (!Uri.TryCreate(fileUri, UriKind.Absolute, out var uri))
        {
            return;
        }

        var filePath = uri.LocalPath;
        if (!File.Exists(filePath))
        {
            return;
        }

        if (Package.GetGlobalService(typeof(EnvDTE.DTE)) is not EnvDTE.DTE dte)
        {
            return;
        }

        var window = dte.ItemOperations.OpenFile(filePath);
        if (window?.Document?.Selection is not EnvDTE.TextSelection selection)
        {
            return;
        }

        var targetLine = Math.Max(1, line + 1);
        var targetColumn = Math.Max(1, character + 1);
        selection.MoveToLineAndOffset(targetLine, targetColumn, false);
        selection.MoveToLineAndOffset(targetLine, targetColumn, true);
    }

    internal static async Task OpenSqlAtQueryAsync(string fileUri, int line, int character)
    {
        if (!Uri.TryCreate(fileUri, UriKind.Absolute, out var uri))
        {
            return;
        }

        var hover = await TryGetStructuredHoverAsync(uri.LocalPath, line, character, CancellationToken.None);
        var sql = hover?.EnrichedSql;
        if (string.IsNullOrWhiteSpace(sql) && hover?.Statements is { Count: > 0 })
        {
            sql = hover.Statements[0].Sql;
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            await NavigateToQuerySourceAsync(fileUri, line, character);
            return;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        LinqHoverMarkdownRenderer.TryOpenSqlInEditorPublic(sql);
    }

    internal static async Task RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        var client = Current;
        if (client?.rpc is not JsonRpc languageServerRpc)
        {
            QueryLensStatusBarManager.Reset();
            return;
        }

        try
        {
            var response = await languageServerRpc.InvokeWithParameterObjectAsync<JToken?>(
                QueryLensHostLspMethods.StatusRequest,
                new JObject(),
                cancellationToken);

            var snapshot = response?.ToObject<QueryLensHostStatusSnapshot>();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            QueryLensStatusBarManager.ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            Log($"status-refresh-failed type={ex.GetType().Name} message={ex.Message}");
        }
    }

    internal static async Task RunStartupWarmupAsync(CancellationToken cancellationToken = default)
    {
        var client = Current;
        if (client?.rpc is not JsonRpc languageServerRpc)
        {
            return;
        }

        if (!QueryLensEditorContext.TryGetActiveCSharpContext(out var context, out _))
        {
            Log("warmup-skipped no-active-csharp-editor");
            return;
        }

        try
        {
            Log("warmup-start");
            var response = await languageServerRpc.InvokeWithParameterObjectAsync<JToken?>(
                QueryLensHostLspMethods.WarmupRequest,
                new JObject
                {
                    ["textDocument"] = new JObject { ["uri"] = context!.DocumentUri },
                    ["position"] = new JObject
                    {
                        ["line"] = context.Line,
                        ["character"] = context.Character,
                    },
                },
                cancellationToken);

            var success = response?["success"]?.Value<bool>() == true;
            var assembly = response?["assemblyPath"]?.Value<string>();
            Log($"warmup-finished success={success} assembly={assembly ?? "unknown"}");
            await RefreshStatusAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log($"warmup-failed type={ex.GetType().Name} message={ex.Message}");
        }
    }

    internal static async Task PushRuntimeConfigurationAsync()
    {
        var client = Current;
        if (client?.rpc is not JsonRpc languageServerRpc)
        {
            return;
        }

        try
        {
            await languageServerRpc.NotifyWithParameterObjectAsync(
                "workspace/didChangeConfiguration",
                JObject.FromObject(QueryLensHostInitializationOptions.WrapForConfigurationChange(BuildRuntimeOptions())));
        }
        catch (Exception ex)
        {
            Log($"runtime-config-push-failed type={ex.GetType().Name} message={ex.Message}");
        }
    }
}
