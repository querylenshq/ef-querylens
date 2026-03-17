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
    private readonly Lock _gate = new();
    private int _backgroundCleanupScheduled;
    private DateTime _lastCleanupUtc = DateTime.MinValue;

    public ShadowAssemblyCache(bool debugEnabled)
    {
        _debugEnabled = debugEnabled;
        _root = ResolveShadowRoot();
        _bundleRoot = Path.Combine(_root, "bundles");
        _stagingRoot = Path.Combine(_root, "staging");

        Directory.CreateDirectory(_bundleRoot);
        Directory.CreateDirectory(_stagingRoot);
    }

    private static string ResolveShadowRoot()
    {
        var envOverride = Environment.GetEnvironmentVariable("QUERYLENS_SHADOW_ROOT");
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            return envOverride.Trim();
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EFQueryLens",
            "shadow");
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

        var manifest = BuildManifest(sourceDir);
        var bundleKey = ComputeBundleKey(sourceDir, manifest);
        var bundlePath = Path.Combine(_bundleRoot, bundleKey);
        var bundleAssemblyPath = Path.Combine(bundlePath, Path.GetFileName(fullSourcePath));
        bool shouldScheduleCleanup;
        var forceCleanup = false;

        if (File.Exists(bundleAssemblyPath))
        {
            lock (_gate)
            {
                TouchDirectory(bundlePath);
                shouldScheduleCleanup = IsCleanupDue(DateTime.UtcNow, force: false);
            }

            if (shouldScheduleCleanup)
            {
                ScheduleCleanupIfDue(force: false);
            }

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

            lock (_gate)
            {
                if (!File.Exists(bundleAssemblyPath))
                {
                    TryAtomicPromote(stagingPath, bundlePath);
                    forceCleanup = true;
                }

                TouchDirectory(bundlePath);
                shouldScheduleCleanup = IsCleanupDue(DateTime.UtcNow, force: false);
            }
        }
        finally
        {
            TryDeleteDirectory(stagingPath);
        }

        if (forceCleanup)
        {
            ScheduleCleanupIfDue(force: true);
        }
        else if (shouldScheduleCleanup)
        {
            ScheduleCleanupIfDue(force: false);
        }

        return bundleAssemblyPath;
    }

    public void CleanupIfDue(bool force)
    {
        lock (_gate)
        {
            if (!IsCleanupDue(DateTime.UtcNow, force))
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

    private bool IsCleanupDue(DateTime utcNow, bool force)
    {
        if (force)
        {
            return true;
        }

        return utcNow - _lastCleanupUtc >= CleanupInterval;
    }
}
