// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using System;
using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

public sealed class QueryLensOptionsPage : DialogPage
{
    private bool notifyWhenSqlReady = true;
    private int hoverWaitWhenWarmMs = 8000;

    public static QueryLensOptionsPage? Current =>
        QueryLensPackage.Instance?.GetDialogPage(typeof(QueryLensOptionsPage)) as QueryLensOptionsPage;

    [Category("Notifications")]
    [DisplayName("Notify when SQL is ready")]
    [Description("Show a notification when background SQL translation completes after a queued hover.")]
    public bool NotifyWhenSqlReady
    {
        get => notifyWhenSqlReady;
        set
        {
            if (notifyWhenSqlReady == value)
            {
                return;
            }

            notifyWhenSqlReady = value;
            _ = QueryLensLanguageClient.PushRuntimeConfigurationAsync();
        }
    }

    [Category("Hover")]
    [DisplayName("Hover wait when warm (ms)")]
    [Description("Maximum time to wait for SQL when the assembly is already warmed.")]
    public int HoverWaitWhenWarmMs
    {
        get => hoverWaitWhenWarmMs;
        set
        {
            var clamped = Math.Max(0, Math.Min(30_000, value));
            if (hoverWaitWhenWarmMs == clamped)
            {
                return;
            }

            hoverWaitWhenWarmMs = clamped;
            _ = QueryLensLanguageClient.PushRuntimeConfigurationAsync();
        }
    }
}
