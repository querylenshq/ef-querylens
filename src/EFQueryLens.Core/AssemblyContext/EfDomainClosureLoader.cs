using System.Reflection;
using System.Text.Json;
using EFQueryLens.Core.Scaffolding;

namespace EFQueryLens.Core.AssemblyContext;

/// <summary>
/// Builds the positive assembly closure for EF translation from <c>deps.json</c>:
/// host + project-reference libraries + EF ecosystem packages (and their runtime deps).
/// </summary>
public static class EfDomainClosureLoader
{
    public sealed record EfDomainClosure(
        IReadOnlySet<string> AssemblySimpleNames,
        string HostAssemblySimpleName);

    public static bool TryBuildClosure(
        string hostDllPath,
        string? dbContextTypeName,
        out EfDomainClosure closure)
    {
        closure = new EfDomainClosure(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            Path.GetFileNameWithoutExtension(hostDllPath));

        if (string.IsNullOrWhiteSpace(hostDllPath) || !File.Exists(hostDllPath))
            return false;

        var depsPath = Path.ChangeExtension(hostDllPath, ".deps.json");
        if (!File.Exists(depsPath))
            return false;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(depsPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("targets", out var targets)
                || !root.TryGetProperty("libraries", out var libraries))
            {
                return false;
            }

            var tfm = ResolveTargetFramework(root, targets);
            if (tfm is null
                || !targets.TryGetProperty(tfm, out var tfmTargets))
            {
                return false;
            }

            var libraryTypes = ParseLibraryTypes(libraries);
            var hostName = closure.HostAssemblySimpleName;
            var hostLibKey = FindHostLibraryKey(tfmTargets, hostName);
            if (hostLibKey is null)
                return false;

            var assemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { hostName };
            var visitedLibs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(hostLibKey);

            while (queue.Count > 0)
            {
                var libKey = queue.Dequeue();
                if (!visitedLibs.Add(libKey))
                    continue;

                if (!tfmTargets.TryGetProperty(libKey, out var libTarget))
                    continue;

                foreach (var asmName in ExtractRuntimeAssemblyNames(libTarget))
                {
                    assemblyNames.Add(asmName);
                }

                if (!libTarget.TryGetProperty("dependencies", out var dependencies))
                    continue;

                var currentIsProject = libraryTypes.TryGetValue(libKey, out var libType)
                    && string.Equals(libType, "project", StringComparison.OrdinalIgnoreCase);
                var currentIsEfPackage = IsEfEcosystemLibraryKey(libKey, libraryTypes);
                var currentIsHost = string.Equals(libKey, hostLibKey, StringComparison.OrdinalIgnoreCase);

                foreach (var dep in dependencies.EnumerateObject())
                {
                    var depLibKey = ResolveLibraryKey(dep.Name, dep.Value.GetString(), libraries);
                    if (depLibKey is null || visitedLibs.Contains(depLibKey))
                        continue;

                    if (!libraryTypes.TryGetValue(depLibKey, out var depType))
                        continue;

                    // Referenced class libraries bring their full NuGet graph (Ardalis, DTO libs, …).
                    // The host executable only seeds project refs + EF packages (not NSwag / OpenAPI).
                    if (currentIsProject && !currentIsHost)
                    {
                        queue.Enqueue(depLibKey);
                        continue;
                    }

                    var depIsProject = string.Equals(depType, "project", StringComparison.OrdinalIgnoreCase);
                    var depIsEf = IsEfEcosystemLibraryKey(depLibKey, libraryTypes);

                    if (depIsProject || depIsEf)
                    {
                        queue.Enqueue(depLibKey);
                        continue;
                    }

                    if (currentIsEfPackage
                        && string.Equals(depType, "package", StringComparison.OrdinalIgnoreCase))
                    {
                        queue.Enqueue(depLibKey);
                    }
                }
            }

            closure = new EfDomainClosure(assemblyNames, hostName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveTargetFramework(JsonElement root, JsonElement targets)
    {
        if (root.TryGetProperty("runtimeTarget", out var runtimeTarget)
            && runtimeTarget.TryGetProperty("name", out var nameProp))
        {
            var name = nameProp.GetString();
            if (!string.IsNullOrWhiteSpace(name) && targets.TryGetProperty(name, out _))
                return name;
        }

        foreach (var tfm in targets.EnumerateObject())
        {
            return tfm.Name;
        }

        return null;
    }

    private static Dictionary<string, string> ParseLibraryTypes(JsonElement libraries)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in libraries.EnumerateObject())
        {
            if (entry.Value.TryGetProperty("type", out var typeProp))
            {
                var type = typeProp.GetString();
                if (!string.IsNullOrWhiteSpace(type))
                    result[entry.Name] = type;
            }
        }

        return result;
    }

    private static string? FindHostLibraryKey(JsonElement tfmTargets, string hostAssemblySimpleName)
    {
        var hostDll = hostAssemblySimpleName + ".dll";
        foreach (var entry in tfmTargets.EnumerateObject())
        {
            if (!entry.Value.TryGetProperty("runtime", out var runtime))
                continue;

            foreach (var runtimeEntry in runtime.EnumerateObject())
            {
                if (string.Equals(Path.GetFileName(runtimeEntry.Name), hostDll, StringComparison.OrdinalIgnoreCase))
                    return entry.Name;
            }
        }

        return null;
    }

    private static IEnumerable<string> ExtractRuntimeAssemblyNames(JsonElement libTarget)
    {
        if (!libTarget.TryGetProperty("runtime", out var runtime))
            yield break;

        foreach (var runtimeEntry in runtime.EnumerateObject())
        {
            var fileName = Path.GetFileName(runtimeEntry.Name);
            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return Path.GetFileNameWithoutExtension(fileName);
        }
    }

    private static string? ResolveLibraryKey(
        string dependencyName,
        string? version,
        JsonElement libraries)
    {
        if (!string.IsNullOrWhiteSpace(version))
        {
            var direct = $"{dependencyName}/{version}";
            if (libraries.TryGetProperty(direct, out _))
                return direct;
        }

        foreach (var entry in libraries.EnumerateObject())
        {
            var slash = entry.Name.IndexOf('/');
            if (slash <= 0)
                continue;

            if (string.Equals(entry.Name[..slash], dependencyName, StringComparison.OrdinalIgnoreCase))
                return entry.Name;
        }

        return null;
    }

    private static bool IsEfEcosystemLibraryKey(
        string libKey,
        IReadOnlyDictionary<string, string> libraryTypes)
    {
        var slash = libKey.IndexOf('/');
        var packageName = slash > 0 ? libKey[..slash] : libKey;
        return libraryTypes.TryGetValue(libKey, out var type)
            && string.Equals(type, "package", StringComparison.OrdinalIgnoreCase)
            && EfPackageRegistry.IsEfEcosystemPackage(packageName);
    }

    public static IReadOnlyList<AssemblyName> ToAssemblyNames(IEnumerable<string> simpleNames)
    {
        return simpleNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => new AssemblyName(n))
            .ToArray();
    }
}
