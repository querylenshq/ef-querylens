using System.Collections.Concurrent;
using System.Reflection;

namespace EFQueryLens.Core.AssemblyContext;

/// <summary>
/// Shared reflection helpers for scanning types from user assemblies loaded in isolated ALCs.
/// Handles <see cref="ReflectionTypeLoadException"/> consistently and caches per-assembly manifests.
/// </summary>
public static class AssemblyReflection
{
    private static readonly ConcurrentDictionary<string, CachedManifest> TypeManifestCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] DefaultFrameworkExcludePrefixes =
    [
        "System.",
        "Microsoft.Extensions.",
        "Microsoft.AspNetCore.",
        "Microsoft.CodeAnalysis",
    ];

    /// <summary>
    /// Optional assemblies skipped during layer-C reflection scans (not Roslyn metadata).
    /// </summary>
    private static readonly string[] OptionalReflectionScanExcludePrefixes =
    [
        "NSwag.",
        "Swashbuckle.",
    ];

    public readonly struct ScanOptions
    {
        public bool PublicOnly { get; init; }
        public IReadOnlyList<string>? ExcludeAssemblyNamePrefixes { get; init; }
        public Action<string>? OnDiagnostic { get; init; }
    }

    /// <summary>
    /// Returns loadable types from <paramref name="assembly"/>, using <see cref="ReflectionTypeLoadException.Types"/>
    /// when a full scan fails.
    /// </summary>
    public static IReadOnlyList<Type> GetLoadableTypes(Assembly assembly, ScanOptions options = default)
    {
        if (ShouldSkipAssembly(assembly, options.ExcludeAssemblyNamePrefixes))
            return [];

        IEnumerable<Type> types;
        try
        {
            types = options.PublicOnly ? assembly.GetExportedTypes() : assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException rtle)
        {
            RecordLoaderDiagnostics(assembly, rtle.LoaderExceptions, options.OnDiagnostic);
            types = rtle.Types.Where(t => t is not null).Cast<Type>();
        }
        catch (Exception ex) when (IsIgnorableReflectionFailure(ex))
        {
            return [];
        }
        catch (Exception ex)
        {
            options.OnDiagnostic?.Invoke(
                $"Could not scan '{assembly.GetName().Name}' for types: {ex.Message}");
            return [];
        }

        if (!options.PublicOnly)
            return types.ToArray();

        return types.Where(t => t.IsPublic || t.IsNestedPublic).ToArray();
    }

    /// <summary>
    /// Cached variant of <see cref="GetLoadableTypes"/> keyed by assembly location and last-write time.
    /// </summary>
    public static IReadOnlyList<Type> GetCachedLoadableTypes(Assembly assembly, ScanOptions options = default)
    {
        if (ShouldSkipAssembly(assembly, options.ExcludeAssemblyNamePrefixes))
            return [];

        var cacheKey = BuildManifestCacheKey(assembly);
        if (TypeManifestCache.TryGetValue(cacheKey, out var cached)
            && cached.OptionsKey == BuildOptionsKey(options))
        {
            return cached.Types;
        }

        var types = GetLoadableTypes(assembly, options);
        TypeManifestCache[cacheKey] = new CachedManifest(BuildOptionsKey(options), types);
        TrimManifestCache();
        return types;
    }

    /// <summary>
    /// Clears cached type manifests. Used by tests and ALC invalidation.
    /// </summary>
    internal static void ClearManifestCacheForTests() => TypeManifestCache.Clear();

    /// <summary>
    /// True for OpenAPI/Swagger tooling assemblies that may throw during optional type scans.
    /// </summary>
    internal static bool ShouldSkipOptionalReflectionScan(Assembly assembly)
    {
        return ShouldSkipOptionalReflectionScanName(assembly.GetName().Name);
    }

    internal static bool ShouldSkipOptionalReflectionScanName(string? assemblySimpleName)
    {
        if (string.IsNullOrWhiteSpace(assemblySimpleName))
            return false;

        foreach (var prefix in OptionalReflectionScanExcludePrefixes)
        {
            if (assemblySimpleName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ShouldSkipAssembly(Assembly assembly, IReadOnlyList<string>? excludePrefixes)
    {
        if (ShouldSkipOptionalReflectionScan(assembly))
            return true;

        var name = assembly.GetName().Name;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var prefixes = excludePrefixes ?? DefaultFrameworkExcludePrefixes;
        foreach (var prefix in prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string BuildManifestCacheKey(Assembly assembly)
    {
        var location = assembly.Location;
        if (string.IsNullOrWhiteSpace(location))
            return assembly.FullName ?? Guid.NewGuid().ToString("N");

        try
        {
            var ticks = File.GetLastWriteTimeUtc(location).Ticks;
            return $"{location}|{ticks}";
        }
        catch
        {
            return location;
        }
    }

    private static string BuildOptionsKey(ScanOptions options) =>
        (options.PublicOnly ? "p" : "a")
        + "|"
        + string.Join(",", options.ExcludeAssemblyNamePrefixes ?? DefaultFrameworkExcludePrefixes);

    private static void RecordLoaderDiagnostics(
        Assembly assembly,
        Exception?[]? loaderExceptions,
        Action<string>? onDiagnostic)
    {
        if (onDiagnostic is null)
            return;

        var messages = (loaderExceptions ?? [])
            .Where(e => e is not null && !IsIgnorableReflectionFailure(e!))
            .Select(e => e!.Message)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (messages.Count == 0)
            return;

        onDiagnostic(
            $"Partial type load in '{assembly.GetName().Name}': {string.Join("; ", messages)}");
    }

    /// <summary>
    /// Returns true for loader/reflection failures during optional type scans (skip the type).
    /// </summary>
    internal static bool IsIgnorableReflectionFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is ReflectionTypeLoadException rtle)
            {
                var loaders = rtle.LoaderExceptions ?? [];
                return loaders.Length == 0
                    || loaders.All(e => e is null || IsOptionalScanLoaderFailure(e));
            }

            if (current is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOptionalScanLoaderFailure(Exception ex) =>
        ex is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException;

    private static void TrimManifestCache()
    {
        const int maxEntries = 256;
        var overflow = TypeManifestCache.Count - maxEntries;
        if (overflow <= 0)
            return;

        foreach (var key in TypeManifestCache.Keys.Take(overflow))
        {
            TypeManifestCache.TryRemove(key, out _);
        }
    }

    private sealed record CachedManifest(string OptionsKey, IReadOnlyList<Type> Types);
}
