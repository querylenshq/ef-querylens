using System.Text.Json;

namespace EFQueryLens.Core.AssemblyContext;

/// <summary>
/// Full runtime assembly index from a host <c>deps.json</c> (no EF-domain filtering).
/// Used for on-demand ALC loads when CS0246 fires.
/// </summary>
/// <remarks>
/// MSBuild escalation: if CS0246 persists and the defining DLL is not in host <c>bin</c>,
/// implement <c>dotnet msbuild -getItem:ReferencePath</c> (non-copy-local / UseArtifactsOutput).
/// </remarks>
public static class DepsJsonAssemblyIndex
{
    public static bool TryGetAllRuntimeAssemblySimpleNames(
        string hostDllPath,
        out IReadOnlySet<string> assemblySimpleNames)
    {
        assemblySimpleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(hostDllPath) || !File.Exists(hostDllPath))
            return false;

        var depsPath = Path.ChangeExtension(hostDllPath, ".deps.json");
        if (!File.Exists(depsPath))
            return false;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(depsPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("targets", out var targets))
                return false;

            var tfm = ResolveTargetFramework(root, targets);
            if (tfm is null || !targets.TryGetProperty(tfm, out var tfmTargets))
                return false;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var libEntry in tfmTargets.EnumerateObject())
            {
                foreach (var asmName in ExtractRuntimeAssemblyNames(libEntry.Value))
                {
                    names.Add(asmName);
                }
            }

            if (names.Count == 0)
                return false;

            assemblySimpleNames = names;
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
            return tfm.Name;

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
}
