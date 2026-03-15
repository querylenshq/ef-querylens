namespace EFQueryLens.Core.AssemblyContext;

internal sealed partial class ShadowAssemblyCache
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
}
