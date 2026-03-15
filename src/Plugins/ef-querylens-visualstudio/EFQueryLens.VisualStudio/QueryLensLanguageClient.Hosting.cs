// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace EFQueryLens.VisualStudio;

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

internal sealed partial class QueryLensLanguageClient
{
    private static string ResolveWorkspacePath(string extensionDirectory)
    {
        var solutionRoot = TryGetSolutionDirectory();
        if (!string.IsNullOrWhiteSpace(solutionRoot))
        {
            return solutionRoot!;
        }

        var envWorkspace = Environment.GetEnvironmentVariable("QUERYLENS_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(envWorkspace) && Directory.Exists(envWorkspace))
        {
            return Path.GetFullPath(envWorkspace);
        }

        var repoRoot = TryFindRepositoryRoot(extensionDirectory);
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            return repoRoot!;
        }

        return Environment.CurrentDirectory;
    }

    private static string ResolveServerPath(string extensionDirectory, string workspaceRoot)
    {
        var overridePath = Environment.GetEnvironmentVariable(LspDllOverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        var packagedServerPath = Path.Combine(extensionDirectory, "server", "EFQueryLens.Lsp.dll");
        if (File.Exists(packagedServerPath))
        {
            return packagedServerPath;
        }

        var rootServerPath = Path.Combine(extensionDirectory, "EFQueryLens.Lsp.dll");
        if (File.Exists(rootServerPath))
        {
            return rootServerPath;
        }

        var repoRoot = ResolveRepositoryRoot(workspaceRoot, extensionDirectory);
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            var release = Path.Combine(repoRoot, "src", "EFQueryLens.Lsp", "bin", "Release", "net10.0", "EFQueryLens.Lsp.dll");
            if (File.Exists(release))
            {
                return release;
            }

            var debug = Path.Combine(repoRoot, "src", "EFQueryLens.Lsp", "bin", "Debug", "net10.0", "EFQueryLens.Lsp.dll");
            if (File.Exists(debug))
            {
                return debug;
            }

            var published = Path.Combine(repoRoot, "src", "EFQueryLens.Lsp", "bin", "Debug", "net10.0", "publish", "EFQueryLens.Lsp.dll");
            if (File.Exists(published))
            {
                return published;
            }
        }

        return packagedServerPath;
    }

    private static string? ResolveRepositoryRoot(string workspaceRoot, string extensionDirectory)
    {
        var overrideRoot = Environment.GetEnvironmentVariable(RepositoryRootOverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            var normalized = Path.GetFullPath(overrideRoot);
            if (File.Exists(Path.Combine(normalized, "EFQueryLens.slnx")))
            {
                return normalized;
            }
        }

        if (File.Exists(Path.Combine(workspaceRoot, "EFQueryLens.slnx")))
        {
            return workspaceRoot;
        }

        return TryFindRepositoryRoot(extensionDirectory);
    }

    private static string? TryFindRepositoryRoot(string startDirectory)
    {
        try
        {
            var current = new DirectoryInfo(startDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "EFQueryLens.slnx")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }
        catch
        {
            // Best effort only.
        }

        return null;
    }

    private static string BuildLspLogFilePath(string workspaceRoot)
    {
        var normalizedWorkspace = Path.GetFullPath(workspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        byte[] hashBytes;
        using (var sha = SHA256.Create())
        {
            hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalizedWorkspace));
        }

        var hash = BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
        if (hash.Length > 16)
        {
            hash = hash.Substring(0, 16);
        }

        var directory = Path.Combine(Path.GetTempPath(), "EFQueryLens", "vs-logs");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"lsp-{hash}.log");
    }

    private static string? TryGetSolutionDirectory()
    {
        try
        {
            return ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var solution = Package.GetGlobalService(typeof(SVsSolution)) as IVsSolution;
                if (solution is null)
                {
                    return null;
                }

                solution.GetSolutionInfo(out var solutionDirectory, out _, out _);
                if (string.IsNullOrWhiteSpace(solutionDirectory))
                {
                    return null;
                }

                return Path.GetFullPath(solutionDirectory);
            });
        }
        catch
        {
            return null;
        }
    }

}
