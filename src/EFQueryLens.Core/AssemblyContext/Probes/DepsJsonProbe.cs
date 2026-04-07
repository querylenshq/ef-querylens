using System.Reflection;

namespace EFQueryLens.Core.AssemblyContext.Probes;

/// <summary>
/// Resolves assemblies via the project's <c>.deps.json</c>-backed
/// <see cref="System.Runtime.Loader.AssemblyDependencyResolver"/>.
/// When the resolver returns a reference-assembly path, attempts to locate
/// the matching runtime binary.
/// </summary>
internal sealed class DepsJsonProbe : AssemblyProbe
{
    private readonly System.Runtime.Loader.AssemblyDependencyResolver _resolver;

    internal DepsJsonProbe(System.Runtime.Loader.AssemblyDependencyResolver resolver)
    {
        _resolver = resolver;
    }

    internal override string? TryResolve(AssemblyName assemblyName)
    {
        var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
        if (resolved is null)
            return null;

        if (!LooksLikeReferenceAssemblyPath(resolved))
            return resolved;

        if (string.IsNullOrWhiteSpace(assemblyName.Name))
            return null;

        return TryResolveRuntimeAssemblyPathFromReference(resolved, assemblyName.Name);
    }

    internal static bool LooksLikeReferenceAssemblyPath(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return false;

        var normalized = assemblyPath.Replace('\\', '/');
        return normalized.Contains("/ref/", StringComparison.OrdinalIgnoreCase);
    }

    internal static string? TryResolveRuntimeAssemblyPathFromReference(
        string referenceAssemblyPath,
        string assemblySimpleName)
    {
        if (string.IsNullOrWhiteSpace(referenceAssemblyPath)
            || string.IsNullOrWhiteSpace(assemblySimpleName))
        {
            return null;
        }

        try
        {
            var normalized = referenceAssemblyPath.Replace('\\', '/');
            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var refIndex = Array.FindIndex(parts,
                p => string.Equals(p, "ref", StringComparison.OrdinalIgnoreCase));

            if (refIndex <= 0 || refIndex >= parts.Length - 1)
                return null;

            var packageRoot = string.Join(Path.DirectorySeparatorChar, parts.Take(refIndex));
            var tfm = parts[refIndex + 1];
            var fileName = assemblySimpleName + ".dll";

            var directTfmCandidate = Path.Combine(packageRoot, "lib", tfm, fileName);
            if (File.Exists(directTfmCandidate))
                return directTfmCandidate;

            var libRoot = Path.Combine(packageRoot, "lib");
            if (!Directory.Exists(libRoot))
                return null;

            var candidates = Directory.EnumerateFiles(libRoot, fileName, SearchOption.AllDirectories)
                .Where(path => !LooksLikeReferenceAssemblyPath(path))
                .OrderByDescending(path => path.Contains(Path.DirectorySeparatorChar + tfm + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return candidates.Length > 0 ? candidates[0] : null;
        }
        catch
        {
            return null;
        }
    }
}
