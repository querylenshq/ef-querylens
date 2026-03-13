using System.Security.Cryptography;
using System.Text;

namespace EFQueryLens.Core.AssemblyContext;

internal sealed class ShadowAssemblyCache
{
    private const int DefaultShadowCacheMaxAgeHours = 12;
    private const long DefaultShadowCacheSoftLimitBytes = 5L * 1024L * 1024L * 1024L;
    private const long DefaultShadowCacheTargetBytes = 3L * 1024L * 1024L * 1024L;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(15);

    private readonly bool _debugEnabled;
    private readonly string _root;
    private readonly string _bundleRoot;
    private readonly string _stagingRoot;
    private readonly object _gate = new();
    private int _backgroundCleanupScheduled;
    private DateTime _lastCleanupUtc = DateTime.MinValue;

    public ShadowAssemblyCache(bool debugEnabled)
    {
        _debugEnabled = debugEnabled;
        _root = Path.Combine(Path.GetTempPath(), "EFQueryLens", "shadow");
        _bundleRoot = Path.Combine(_root, "bundles");
        _stagingRoot = Path.Combine(_root, "staging");

        Directory.CreateDirectory(_bundleRoot);
        Directory.CreateDirectory(_stagingRoot);
    }

    public string ResolveOrCreateBundle(string sourceAssemblyPath)
    {
        var fullSourcePath = Path.GetFullPath(sourceAssemblyPath);
        var sourceDir = Path.GetDirectoryName(fullSourcePath)
            ?? throw new InvalidOperationException($"Could not determine source directory for '{fullSourcePath}'.");

        if (!Directory.Exists(sourceDir))
        {
            throw new DirectoryNotFoundException($"Source output directory not found: {sourceDir}");
        }

        lock (_gate)
        {
            CleanupIfDue(force: false);

            var manifest = BuildManifest(sourceDir);
            var bundleKey = ComputeBundleKey(sourceDir, manifest);
            var bundlePath = Path.Combine(_bundleRoot, bundleKey);
            var bundleAssemblyPath = Path.Combine(bundlePath, Path.GetFileName(fullSourcePath));

            if (File.Exists(bundleAssemblyPath))
            {
                TouchDirectory(bundlePath);
                return bundleAssemblyPath;
            }

            var stagingPath = Path.Combine(_stagingRoot, $"{bundleKey}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingPath);

            try
            {
                foreach (var entry in manifest)
                {
                    var targetPath = Path.Combine(stagingPath, entry.RelativePath);
                    var targetDir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrWhiteSpace(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    File.Copy(entry.FullPath, targetPath, overwrite: true);
                }

                TryAtomicPromote(stagingPath, bundlePath);
                TouchDirectory(bundlePath);
                CleanupIfDue(force: true);
                return Path.Combine(bundlePath, Path.GetFileName(fullSourcePath));
            }
            finally
            {
                TryDeleteDirectory(stagingPath);
            }
        }
    }

    public void CleanupIfDue(bool force)
    {
        lock (_gate)
        {
            if (!force && DateTime.UtcNow - _lastCleanupUtc < CleanupInterval)
            {
                return;
            }

            CleanupCore();
            _lastCleanupUtc = DateTime.UtcNow;
        }
    }

    public void ScheduleCleanupIfDue(bool force)
    {
        if (Interlocked.Exchange(ref _backgroundCleanupScheduled, 1) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                CleanupIfDue(force);
            }
            finally
            {
                Volatile.Write(ref _backgroundCleanupScheduled, 0);
            }
        });
    }

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

            // Always clear stale staging folders first.
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

    private static List<ManifestEntry> BuildManifest(string sourceDirectory)
    {
        return Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new ManifestEntry(
                    Path.GetFullPath(path),
                    Path.GetRelativePath(sourceDirectory, path),
                    info.Length,
                    info.LastWriteTimeUtc.Ticks);
            })
            .OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ComputeBundleKey(string sourceDirectory, IReadOnlyList<ManifestEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFullPath(sourceDirectory)).Append('|');
        foreach (var entry in entries)
        {
            sb.Append(entry.RelativePath).Append('|')
              .Append(entry.Length).Append('|')
              .Append(entry.LastWriteTicksUtc).Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    private static void TryAtomicPromote(string stagingPath, string finalPath)
    {
        try
        {
            Directory.Move(stagingPath, finalPath);
            return;
        }
        catch (IOException)
        {
            if (Directory.Exists(finalPath))
            {
                return;
            }

            throw;
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

    private sealed record ManifestEntry(string FullPath, string RelativePath, long Length, long LastWriteTicksUtc);
}
