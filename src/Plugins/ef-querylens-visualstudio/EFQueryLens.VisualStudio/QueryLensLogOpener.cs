// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

internal static class QueryLensLogOpener
{
    private static readonly object tailSync = new();
    private static readonly Guid outputPaneGuid = new("B7A8AF5E-4B7A-4D4D-8E42-520A6CB3A4D2");
    private static readonly TimeSpan tailInterval = TimeSpan.FromSeconds(1);

    private static Timer? tailTimer;
    private static IVsOutputWindowPane? outputPane;
    private static readonly Dictionary<string, long> activeLogPositions = new(StringComparer.OrdinalIgnoreCase);
    private static bool paneInitialized;

    internal static async Task InitializeOutputPaneAsync(AsyncPackage package, CancellationToken cancellationToken)
    {
        var pane = await EnsureOutputPaneAsync(package, cancellationToken);
        if (pane is null)
        {
            return;
        }

        await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        lock (tailSync)
        {
            if (paneInitialized)
            {
                return;
            }

            paneInitialized = true;
            outputPane = pane;
        }

        pane.OutputString($"EF QueryLens output initialized ({DateTime.UtcNow:O}){Environment.NewLine}");
    }

    internal static void WriteClientDiagnosticLine(string message)
    {
        IVsOutputWindowPane? pane;
        lock (tailSync)
        {
            pane = outputPane;
        }

        if (pane is null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
#pragma warning disable VSTHRD010
            pane.OutputStringThreadSafe($"[VS-Client] {message}{Environment.NewLine}");
#pragma warning restore VSTHRD010
        }
        catch
        {
            // Best effort only.
        }
    }

    internal static async Task<(bool Success, string Message)> StartTailInOutputWindowAsync(AsyncPackage package, CancellationToken cancellationToken)
    {
        var candidates = QueryLensLanguageClient.GetLogFilePaths()
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            return (false, "No QueryLens log file path is available yet. Trigger a hover first.");
        }

        foreach (var path in candidates)
        {
            EnsureFileExists(path);
        }

        var pane = await EnsureOutputPaneAsync(package, cancellationToken);
        if (pane is null)
        {
            return (false, "Failed to access Visual Studio Output window pane.");
        }

        await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        pane.Activate();
        pane.OutputString($"{Environment.NewLine}=== EF QueryLens log tail started ({DateTime.UtcNow:O}) ==={Environment.NewLine}");

        lock (tailSync)
        {
            activeLogPositions.Clear();
            outputPane = pane;

            foreach (var path in candidates)
            {
                var label = ResolveLogLabel(path);
                pane.OutputString($"{Environment.NewLine}--- {label}: {path} ---{Environment.NewLine}");

                var snapshot = ReadLastLines(path, maxLines: 80);
                if (!string.IsNullOrWhiteSpace(snapshot))
                {
                    pane.OutputString($"--- Last 80 lines ---{Environment.NewLine}");
                    pane.OutputString(snapshot + Environment.NewLine);
                }

                activeLogPositions[path] = GetFileLength(path);
            }

            pane.OutputString("--- Live tail ---" + Environment.NewLine);

            if (tailTimer is null)
            {
                tailTimer = new Timer(static _ => TailTick(), null, tailInterval, tailInterval);
            }
            else
            {
                tailTimer.Change(tailInterval, tailInterval);
            }
        }

        var summary = string.Join(", ", candidates.Select(ResolveLogLabel));
        return (true, $"Tailing: {summary}");
    }

    internal static void StopTail()
    {
        lock (tailSync)
        {
            if (tailTimer is not null)
            {
                try
                {
                    tailTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                }
                catch
                {
                    // Best effort only.
                }

                try
                {
                    tailTimer.Dispose();
                }
                catch
                {
                    // Best effort only.
                }

                tailTimer = null;
            }

            activeLogPositions.Clear();
        }
    }

    private static string ResolveLogLabel(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Equals("EFQueryLens.VisualStudio.log", StringComparison.OrdinalIgnoreCase))
        {
            return "VS Client";
        }

        if (fileName.StartsWith("lsp-", StringComparison.OrdinalIgnoreCase))
        {
            return "LSP";
        }

        return fileName;
    }

    private static void EnsureFileExists(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(path))
        {
            File.WriteAllText(path, $"EF QueryLens Log{Environment.NewLine}Created (UTC): {DateTime.UtcNow:O}{Environment.NewLine}");
        }
    }

    private static async Task<IVsOutputWindowPane?> EnsureOutputPaneAsync(AsyncPackage package, CancellationToken cancellationToken)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var outputWindow = await package.GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;
        if (outputWindow is null)
        {
            return null;
        }

        var paneGuid = outputPaneGuid;
        outputWindow.CreatePane(ref paneGuid, "EF QueryLens", 1, 1);
        outputWindow.GetPane(ref paneGuid, out var pane);
        return pane;
    }

    private static void TailTick()
    {
        Dictionary<string, long> paths;
        IVsOutputWindowPane? pane;

        lock (tailSync)
        {
            paths = new Dictionary<string, long>(activeLogPositions, StringComparer.OrdinalIgnoreCase);
            pane = outputPane;
        }

        if (pane is null || paths.Count == 0)
        {
            return;
        }

        foreach (var entry in paths)
        {
            var path = entry.Key;
            var position = entry.Value;
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var readPosition = position;
                if (stream.Length < readPosition)
                {
                    readPosition = 0;
                    var label = ResolveLogLabel(path);
#pragma warning disable VSTHRD010
                    pane.OutputStringThreadSafe($"{Environment.NewLine}--- {label} log rotated/truncated ---{Environment.NewLine}");
#pragma warning restore VSTHRD010
                }

                if (stream.Length == readPosition)
                {
                    continue;
                }

                stream.Seek(readPosition, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                var appended = reader.ReadToEnd();
                var nextPosition = stream.Position;

                if (!string.IsNullOrEmpty(appended))
                {
                    var label = ResolveLogLabel(path);
#pragma warning disable VSTHRD010
                    pane.OutputStringThreadSafe($"[{label}] {appended}");
#pragma warning restore VSTHRD010
                }

                lock (tailSync)
                {
                    if (activeLogPositions.ContainsKey(path))
                    {
                        activeLogPositions[path] = nextPosition;
                    }
                }
            }
            catch
            {
                // Best effort tailing.
            }
        }
    }

    private static long GetFileLength(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            return fileInfo.Exists ? fileInfo.Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string ReadLastLines(string path, int maxLines)
    {
        try
        {
            var tail = new Queue<string>(maxLines);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (tail.Count == maxLines)
                {
                    _ = tail.Dequeue();
                }

                tail.Enqueue(line);
            }

            return string.Join(Environment.NewLine, tail);
        }
        catch
        {
            return string.Empty;
        }
    }
}
