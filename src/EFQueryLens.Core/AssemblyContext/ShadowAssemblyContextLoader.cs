using EFQueryLens.Core.Common;

namespace EFQueryLens.Core.AssemblyContext;

/// <summary>
/// Loads user assemblies via the shared <see cref="ShadowAssemblyCache"/> so bin DLLs are
/// never locked by QueryLens hosts (daemon, CLI setup, LSP).
/// </summary>
public static class ShadowAssemblyContextLoader
{
    private static Lazy<ShadowAssemblyCache> SharedCache = new(CreateSharedCache);
    private static readonly object Gate = new();

    internal static ShadowAssemblyCache Cache => SharedCache.Value;

    /// <summary>
    /// Test-only hook to ensure the shared cache respects environment overrides such as
    /// <c>QUERYLENS_SHADOW_ROOT</c>. Without this, the shared lazy cache may already be
    /// initialized by earlier tests in the same process.
    /// </summary>
    internal static void ResetSharedCacheForTests()
    {
        lock (Gate)
        {
            SharedCache = new(CreateSharedCache);
        }
    }

    /// <summary>
    /// Resolves or creates a shadow copy of the source assembly bundle and returns an isolated
    /// <see cref="ProjectAssemblyContext"/> over the shadow DLL path.
    /// </summary>
    public static ProjectAssemblyContext Create(string sourceAssemblyPath)
    {
        var shadowPath = ResolveShadowAssemblyPath(sourceAssemblyPath);
        return ProjectAssemblyContextFactory.Create(shadowPath);
    }

    /// <summary>
    /// Returns the shadow DLL path for the given source assembly without creating a context.
    /// </summary>
    public static string ResolveShadowAssemblyPath(string sourceAssemblyPath) =>
        SharedCache.Value.ResolveOrCreateBundle(Path.GetFullPath(sourceAssemblyPath));

    /// <summary>
    /// Returns the bundle key for a source assembly output directory without creating a shadow copy.
    /// </summary>
    public static string ComputeBundleKey(string sourceAssemblyPath) =>
        SharedCache.Value.ComputeBundleKeyForSourceAssembly(Path.GetFullPath(sourceAssemblyPath));

    private static ShadowAssemblyCache CreateSharedCache()
    {
        var debugEnabled = EnvironmentVariableParser.ReadBool("QUERYLENS_DEBUG", fallback: false);
        var cache = new ShadowAssemblyCache(debugEnabled);
        cache.RunStartupCleanup();
        return cache;
    }
}
