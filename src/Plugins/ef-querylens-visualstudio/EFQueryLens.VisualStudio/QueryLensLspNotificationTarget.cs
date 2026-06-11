// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using EFQueryLens.VisualStudio.Host.Contracts;
using StreamJsonRpc;

internal sealed class QueryLensLspNotificationTarget
{
    [JsonRpcMethod(QueryLensHostLspMethods.SqlReadyNotification, UseSingleObjectParameterDeserialization = true)]
    public void OnSqlReady(QueryLensHostSqlReadyNotification notification)
    {
        QueryLensLanguageClient.LogSqlReadyDiagnostic("sql-ready-jsonrpc-dispatch");
        QueryLensLanguageClient.HandleSqlReadyNotification(notification);
    }

    [JsonRpcMethod(QueryLensHostLspMethods.StatusChangedNotification, UseSingleObjectParameterDeserialization = true)]
    public void OnStatusChanged(QueryLensHostStatusSnapshot snapshot)
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            QueryLensStatusBarManager.ApplySnapshot(snapshot);
        });
    }
}
