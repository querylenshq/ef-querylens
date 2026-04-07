using System.Collections.Concurrent;
using EFQueryLens.Core.Daemon;

namespace EFQueryLens.Lsp.Parsing;

internal static class ProjectKeyHelper
{
    private static readonly ConcurrentDictionary<string, string> FileProjectKeyCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static string GetProjectKey(string sourceFilePath)
    {
        var normalizedSourcePath = Path.GetFullPath(sourceFilePath);
        if (FileProjectKeyCache.TryGetValue(normalizedSourcePath, out var cached))
        {
            return cached;
        }

        var currentDir = Path.GetDirectoryName(normalizedSourcePath);
        while (!string.IsNullOrWhiteSpace(currentDir))
        {
            var hasProject = Directory.EnumerateFiles(currentDir, "*.csproj", SearchOption.TopDirectoryOnly)
                .Any();
            if (hasProject)
            {
                var key = DaemonWorkspaceIdentity.ComputeWorkspaceHash(currentDir);
                FileProjectKeyCache[normalizedSourcePath] = key;
                return key;
            }

            currentDir = Directory.GetParent(currentDir)?.FullName;
        }

        var defaultBasePath = Path.GetDirectoryName(normalizedSourcePath) ?? normalizedSourcePath;
        var defaultProjectKey = DaemonWorkspaceIdentity.ComputeWorkspaceHash(defaultBasePath);
        FileProjectKeyCache[normalizedSourcePath] = defaultProjectKey;
        return defaultProjectKey;
    }
}
