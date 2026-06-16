// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using System;
using System.Threading.Tasks;
using EFQueryLens.VisualStudio.Host.Contracts;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;

/// <summary>
/// Intercepts custom LSP notifications from the server.
/// </summary>
internal sealed class QueryLensLanguageClientMiddleLayer : ILanguageClientMiddleLayer
{
    internal static QueryLensLanguageClientMiddleLayer Instance { get; } = new();

    private QueryLensLanguageClientMiddleLayer()
    {
    }

    public bool CanHandle(string methodName) =>
        string.Equals(methodName, QueryLensHostLspMethods.StatusChangedNotification, StringComparison.Ordinal);

    public Task<JToken> HandleRequestAsync(
        string methodName,
        JToken methodParam,
        Func<JToken, Task<JToken>> sendRequest)
        => sendRequest(methodParam);

    public async Task HandleNotificationAsync(
        string methodName,
        JToken methodParam,
        Func<JToken, Task> sendNotification)
    {
        if (string.Equals(methodName, QueryLensHostLspMethods.StatusChangedNotification, StringComparison.Ordinal))
        {
            var snapshot = methodParam?.ToObject<QueryLensHostStatusSnapshot>();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            QueryLensStatusBarManager.ApplySnapshot(snapshot);
            return;
        }

        await sendNotification(methodParam);
    }
}
