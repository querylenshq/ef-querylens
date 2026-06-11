namespace EFQueryLens.Core.AssemblyContext;

/// <summary>
/// Catalog of runtime assemblies in the host project output directory (layer A metadata).
/// </summary>
internal static class HostBinAssemblyCatalog
{
    internal static bool LooksLikeReferenceAssemblyPath(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return false;

        var normalized = assemblyPath.Replace('\\', '/');
        return normalized.Contains("/ref/", StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string> EnumerateHostBinDllPaths(string hostAssemblyPath)
    {
        var dir = Path.GetDirectoryName(hostAssemblyPath);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return [];

        return Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(path => !LooksLikeReferenceAssemblyPath(path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static HashSet<string> GetHostBinAssemblySimpleNames(string hostAssemblyPath)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateHostBinDllPaths(hostAssemblyPath))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        return names;
    }

    internal static string ComputeBinFingerprint(string hostAssemblyPath)
    {
        var paths = EnumerateHostBinDllPaths(hostAssemblyPath);
        if (paths.Count == 0)
            return "empty";

        long aggregateTicks = 0;
        foreach (var path in paths)
        {
            try
            {
                aggregateTicks ^= File.GetLastWriteTimeUtc(path).Ticks;
            }
            catch
            {
                // Ignore unreadable entries.
            }
        }

        return $"{paths.Count}:{aggregateTicks:x}";
    }
}
