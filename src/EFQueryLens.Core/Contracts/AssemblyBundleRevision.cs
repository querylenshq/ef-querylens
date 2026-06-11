using EFQueryLens.Core.AssemblyContext;

namespace EFQueryLens.Core.Contracts;

/// <summary>
/// Canonical revision token for a compiled host assembly bundle (source output + shadow copy).
/// Shared by LSP invalidation, engine ALC refresh, and daemon translation cache keys.
/// </summary>
public sealed record AssemblyBundleRevision(
    string SourceDllPath,
    string ShadowDllPath,
    string BundleKey,
    long Size,
    long Ticks)
{
    /// <summary>Engine/LSP fingerprint: <c>path|size|ticks|bundleKey</c>.</summary>
    public string SourceFingerprint =>
        $"{Path.GetFullPath(SourceDllPath)}|{Size}|{Ticks}|{BundleKey}";

    /// <summary>Daemon cache segment: <c>bundleKey|size|ticks</c>.</summary>
    public string CacheFingerprint =>
        $"{BundleKey}|{Size}|{Ticks}";

    /// <summary>
    /// Builds a revision from a source DLL path. Returns null when the file is missing.
    /// </summary>
    public static AssemblyBundleRevision? TryBuild(string sourceDllPath)
    {
        if (string.IsNullOrWhiteSpace(sourceDllPath) || !File.Exists(sourceDllPath))
            return null;

        try
        {
            var fullSource = Path.GetFullPath(sourceDllPath);
            var info = new FileInfo(fullSource);
            var shadowDll = ShadowAssemblyContextLoader.ResolveShadowAssemblyPath(fullSource);
            var bundleKey = ShadowAssemblyContextLoader.ComputeBundleKey(fullSource);
            return new AssemblyBundleRevision(
                fullSource,
                shadowDll,
                bundleKey,
                info.Length,
                info.LastWriteTimeUtc.Ticks);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Cheap bundle-key probe without creating a shadow copy. Returns null when unavailable.
    /// </summary>
    public static string? TryPeekBundleKey(string sourceDllPath)
    {
        if (string.IsNullOrWhiteSpace(sourceDllPath) || !File.Exists(sourceDllPath))
            return null;

        try
        {
            return ShadowAssemblyContextLoader.ComputeBundleKey(Path.GetFullPath(sourceDllPath));
        }
        catch
        {
            return null;
        }
    }
}
