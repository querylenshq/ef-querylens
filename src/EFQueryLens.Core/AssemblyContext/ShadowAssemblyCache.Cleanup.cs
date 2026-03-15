namespace EFQueryLens.Core.AssemblyContext;

internal sealed partial class ShadowAssemblyCache
{
    private void CleanupCore()
    {
        try
        {
            if (!Directory.Exists(_root))
            {
                return;
            }

            var maxAgeHours = ReadIntEnvironmentVariable(
                "QUERYLENS_SHADOW_CACHE_MAX_AGE_HOURS",
                DefaultShadowCacheMaxAgeHours,
                min: 1,
                max: 720);

            var softLimitMb = ReadIntEnvironmentVariable(
                "QUERYLENS_SHADOW_CACHE_SOFT_LIMIT_MB",
                (int)(DefaultShadowCacheSoftLimitBytes / (1024L * 1024L)),
                min: 256,
                max: 1024 * 1024);

            var targetMb = ReadIntEnvironmentVariable(
                "QUERYLENS_SHADOW_CACHE_TARGET_MB",
                (int)(DefaultShadowCacheTargetBytes / (1024L * 1024L)),
                min: 128,
                max: softLimitMb);

            var softLimitBytes = softLimitMb * 1024L * 1024L;
            var targetBytes = targetMb * 1024L * 1024L;
            var cutoff = DateTime.UtcNow.AddHours(-maxAgeHours);

            foreach (var stagingDir in Directory.EnumerateDirectories(_stagingRoot))
            {
                DateTime lastWriteUtc;
                try
                {
                    lastWriteUtc = Directory.GetLastWriteTimeUtc(stagingDir);
                }
                catch
                {
                    continue;
                }

                if (lastWriteUtc < cutoff)
                {
                    TryDeleteDirectory(stagingDir);
                }
            }

            var bundleDirs = Directory.EnumerateDirectories(_bundleRoot)
                .Select(path => new DirectoryInfo(path))
                .ToList();

            foreach (var dir in bundleDirs)
            {
                if (dir.LastWriteTimeUtc < cutoff)
                {
                    TryDeleteDirectory(dir.FullName);
                }
            }

            var currentSize = GetDirectorySizeSafe(_bundleRoot);
            if (currentSize > softLimitBytes)
            {
                var oldestFirst = Directory.EnumerateDirectories(_bundleRoot)
                    .Select(path => new DirectoryInfo(path))
                    .OrderBy(d => d.LastWriteTimeUtc)
                    .ToList();

                foreach (var dir in oldestFirst)
                {
                    TryDeleteDirectory(dir.FullName);
                    currentSize = GetDirectorySizeSafe(_bundleRoot);
                    if (currentSize <= targetBytes)
                    {
                        break;
                    }
                }
            }

            if (_debugEnabled)
            {
                Console.Error.WriteLine($"[QL-Engine] shadow-cache-cleanup root={_root}");
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void TouchDirectory(string path)
    {
        try
        {
            Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch
        {
            // Ignore best-effort touch failures.
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
        catch
        {
            // Ignore locked or transient IO failures.
        }
    }

    private static long GetDirectorySizeSafe(string root)
    {
        long total = 0;

        if (!Directory.Exists(root))
        {
            return total;
        }

        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // Ignore per-file access issues.
                    }
                }

                foreach (var dir in Directory.EnumerateDirectories(current))
                {
                    stack.Push(dir);
                }
            }
            catch
            {
                // Ignore per-directory access issues.
            }
        }

        return total;
    }

    private static int ReadIntEnvironmentVariable(string variableName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);
        if (!int.TryParse(raw, out var value))
        {
            return fallback;
        }

        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
