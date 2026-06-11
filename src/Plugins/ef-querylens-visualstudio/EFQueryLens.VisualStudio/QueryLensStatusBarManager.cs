// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using System;
using EFQueryLens.VisualStudio.Host.Contracts;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

internal static class QueryLensStatusBarManager
{
    private static string currentText = "QueryLens: Starting…";

    internal static string CurrentText => currentText;

    internal static void ApplySnapshot(QueryLensHostStatusSnapshot? snapshot)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var mapped = QueryLensHostStatusMapper.Map(snapshot);
        currentText = mapped.Text;
        SetStatusText(mapped.Text, mapped.Tooltip);
    }

    internal static void SetStatusText(string text, string? tooltip = null)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        currentText = text;

        if (Package.GetGlobalService(typeof(SVsStatusbar)) is not IVsStatusbar statusBar)
        {
            return;
        }

        statusBar.SetText(text);
        _ = tooltip;
    }

    internal static void Reset()
    {
        ApplySnapshot(new QueryLensHostStatusSnapshot(QueryLensHostState.Starting, "Starting QueryLens…"));
    }
}
