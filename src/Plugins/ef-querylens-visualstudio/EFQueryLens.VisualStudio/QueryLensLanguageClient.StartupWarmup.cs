// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using EFQueryLens.VisualStudio.Host.Contracts;

internal sealed partial class QueryLensLanguageClient
{
    public object? InitializationOptions =>
        QueryLensHostInitializationOptions.WrapForInitialize(BuildRuntimeOptions());

    private static QueryLensHostInitializationOptions BuildRuntimeOptions()
    {
        var options = QueryLensOptionsPage.Current;
        return new QueryLensHostInitializationOptions
        {
            DebugEnabled = true,
            EnableLspHover = false,
            HoverProgressNotify = false,
            SqlReadyNotify = options?.NotifyWhenSqlReady ?? true,
            HoverProgressDelayMs = 350,
            HoverCacheTtlMs = 15_000,
            MarkdownQueueAdaptiveWaitMs = 200,
            StructuredQueueAdaptiveWaitMs = 200,
            WarmupSuccessTtlMs = 60_000,
            WarmupFailureCooldownMs = 5_000,
            HoverWaitWhenWarmMs = options?.HoverWaitWhenWarmMs ?? 0,
            HoverForegroundResolveBudgetMs = 75,
            HoverFastProbeEnabled = true,
        };
    }
}
