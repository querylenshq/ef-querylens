// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;

internal sealed partial class QueryLensLanguageClient
{
    private static object BuildInitializationOptions()
    {
        return new
        {
            queryLens = new
            {
                debugEnabled = true,
                enableLspHover = false,
                hoverProgressNotify = false,
                hoverProgressDelayMs = 350,
                hoverCacheTtlMs = 15000,
                hoverCancelGraceMs = 1200,
                markdownQueueAdaptiveWaitMs = 200,
                structuredQueueAdaptiveWaitMs = 200,
                warmupSuccessTtlMs = 60000,
                warmupFailureCooldownMs = 5000,
            }
        };
    }

    private static async Task RunStartupPlumbingAsync(CancellationToken cancellationToken)
    {
        try
        {
            var restart = await RequestDaemonRestartAsync(cancellationToken);
            Log($"startup-daemon-restart success={restart.Success} code={restart.Code} message={restart.Message}");

            var warmupRequest = await TryBuildStartupWarmupRequestAsync(cancellationToken);
            if (warmupRequest is null)
            {
                Log("startup-warmup skipped reason=no-csharp-document");
                return;
            }

            var warmup = await RequestWarmupAsync(
                warmupRequest.Value.DocumentUri,
                warmupRequest.Value.Line,
                warmupRequest.Value.Character,
                cancellationToken);

            Log(
                $"startup-warmup success={warmup.Success} " +
                $"uri={warmupRequest.Value.DocumentUri} line={warmupRequest.Value.Line} char={warmupRequest.Value.Character} " +
                $"message={warmup.Message}");
        }
        catch (Exception ex)
        {
            Log($"startup-plumbing-failed type={ex.GetType().Name} message={ex.Message}");
        }
    }

    private static async Task<(string DocumentUri, int Line, int Character)?> TryBuildStartupWarmupRequestAsync(CancellationToken cancellationToken)
    {
        var activeDocumentRequest = await TryGetActiveCSharpDocumentWarmupRequestAsync(cancellationToken);
        if (activeDocumentRequest is not null)
        {
            return activeDocumentRequest;
        }

        var solutionRoot = TryGetSolutionDirectory();
        if (string.IsNullOrWhiteSpace(solutionRoot) || !Directory.Exists(solutionRoot))
        {
            return null;
        }

        foreach (var file in Directory.EnumerateFiles(solutionRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsExcludedPath(file))
            {
                continue;
            }

            var absolute = Path.GetFullPath(file);
            return (new Uri(absolute).AbsoluteUri, 0, 0);
        }

        return null;
    }

    private static async Task<(string DocumentUri, int Line, int Character)?> TryGetActiveCSharpDocumentWarmupRequestAsync(CancellationToken cancellationToken)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        if (Package.GetGlobalService(typeof(EnvDTE.DTE)) is not EnvDTE.DTE dte)
        {
            return null;
        }

        var activeDocument = dte.ActiveDocument;
        if (activeDocument is null || string.IsNullOrWhiteSpace(activeDocument.FullName))
        {
            return null;
        }

        if (!activeDocument.FullName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(activeDocument.FullName))
        {
            return null;
        }

        var line = 0;
        var character = 0;

        if (activeDocument.Selection is EnvDTE.TextSelection selection)
        {
            // DTE positions are 1-based; LSP positions are 0-based.
            line = Math.Max(0, selection.ActivePoint.Line - 1);
            character = Math.Max(0, selection.ActivePoint.DisplayColumn - 1);
        }

        return (new Uri(activeDocument.FullName).AbsoluteUri, line, character);
    }

    private static bool IsExcludedPath(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        return normalized.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("\\.git\\", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("\\.vs\\", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("\\node_modules\\", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
