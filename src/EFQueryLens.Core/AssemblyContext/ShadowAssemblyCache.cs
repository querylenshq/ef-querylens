namespace EFQueryLens.Core.AssemblyContext;

internal sealed partial class ShadowAssemblyCache
{
    private const int DefaultShadowCacheMaxAgeHours = 48;
    private const int DefaultShadowCacheMaxBundles = 20;

    private readonly bool _debugEnabled;
    private readonly string _root;
    private readonly string _bundleRoot;
    private readonly string _stagingRoot;
    private readonly Lock _gate = new();

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

    /// <summary>
    /// Computes the bundle key for the source output directory without creating a shadow copy.
    /// </summary>
    internal string ComputeBundleKeyForSourceAssembly(string sourceAssemblyPath)
    {
        var fullSourcePath = Path.GetFullPath(sourceAssemblyPath);
        var sourceDir = Path.GetDirectoryName(fullSourcePath)
            ?? throw new InvalidOperationException($"Could not determine source directory for '{fullSourcePath}'.");
        if (!Directory.Exists(sourceDir))
        {
            throw new DirectoryNotFoundException($"Source output directory not found: {sourceDir}");
        }

        return ComputeBundleKey(sourceDir, BuildManifest(sourceDir));
    }

    public string ResolveOrCreateBundle(string sourceAssemblyPath) =>
        ResolveOrCreateBundle(sourceAssemblyPath, attempt: 0);

    private string ResolveOrCreateBundle(string sourceAssemblyPath, int attempt)
    {
        var fullSourcePath = Path.GetFullPath(sourceAssemblyPath);
        var sourceDir = Path.GetDirectoryName(fullSourcePath)
            ?? throw new InvalidOperationException($"Could not determine source directory for '{fullSourcePath}'.");

        if (!Directory.Exists(sourceDir))
        {
            throw new DirectoryNotFoundException($"Source output directory not found: {sourceDir}");
        }

        // The shadow root may be deleted between calls (e.g., isolated test roots).
        // Re-create required directories so promotion via Directory.Move never fails.
        Directory.CreateDirectory(_bundleRoot);
        Directory.CreateDirectory(_stagingRoot);

        var manifest = BuildManifest(sourceDir);
        var bundleKey = ComputeBundleKey(sourceDir, manifest);
        var bundlePath = Path.Combine(_bundleRoot, bundleKey);
        var bundleAssemblyPath = Path.Combine(bundlePath, Path.GetFileName(fullSourcePath));

        lock (_gate)
        {
            if (TryGetCompleteBundle(bundleAssemblyPath, bundlePath))
            {
                return bundleAssemblyPath;
            }

            TryDeleteDirectory(bundlePath);

            var stagingPath = Path.Combine(_stagingRoot, $"{bundleKey}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingPath);

            try
            {
                foreach (var entry in FilterCopyManifest(manifest))
                {
                    var targetPath = Path.Combine(stagingPath, entry.RelativePath);
                    var targetDir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrWhiteSpace(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    File.Copy(entry.FullPath, targetPath, overwrite: true);
                }

                if (!File.Exists(bundleAssemblyPath))
                {
                    TryAtomicPromote(stagingPath, bundlePath);
                    Task.Run(() => { try { CleanupCore(); } catch { } });
                }

                TouchDirectory(bundlePath);
            }
            finally
            {
                TryDeleteDirectory(stagingPath);
            }
        }

        if (!IsCompleteBundle(bundleAssemblyPath))
        {
            if (attempt >= 1)
            {
                throw new InvalidOperationException(
                    $"Shadow bundle for '{Path.GetFileName(fullSourcePath)}' is missing executable artifacts.");
            }

            TryDeleteDirectory(bundlePath);
            return ResolveOrCreateBundle(sourceAssemblyPath, attempt + 1);
        }

        return bundleAssemblyPath;

        static bool IsCompleteBundle(string assemblyPath)
        {
            if (!File.Exists(assemblyPath))
            {
                return false;
            }

            var runtimeConfigPath = Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");
            var depsPath = Path.ChangeExtension(assemblyPath, ".deps.json");
            return File.Exists(runtimeConfigPath) && File.Exists(depsPath);
        }

        bool TryGetCompleteBundle(string assemblyPath, string bundleDirectory)
        {
            if (!IsCompleteBundle(assemblyPath))
            {
                return false;
            }

            TouchDirectory(bundleDirectory);
            return true;
        }
    }
}
