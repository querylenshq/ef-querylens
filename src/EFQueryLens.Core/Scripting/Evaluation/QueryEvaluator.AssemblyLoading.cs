using System.Reflection;
using System.Runtime.Loader;
using EFQueryLens.Core.AssemblyContext;

namespace EFQueryLens.Core.Scripting.Evaluation;

public sealed partial class QueryEvaluator
{
    internal static bool IsNoDbContextFoundError(InvalidOperationException ex) =>
        ex is DbContextDiscoveryException { FailureKind: DbContextDiscoveryFailureKind.NoDbContextFound };

    internal static void TryLoadSiblingAssemblies(ProjectAssemblyContext alcCtx)
    {
        var dir = Path.GetDirectoryName(alcCtx.AssemblyPath);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return;

        var loaded = alcCtx.LoadedAssemblies
            .Select(a => a.Location)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var dll in Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
        {
            if (loaded.Contains(dll))
                continue;

            var assemblyName = Path.GetFileNameWithoutExtension(dll);
            if (ProjectAssemblyContext.ShouldPreferDefaultLoadContext(assemblyName))
                continue;

            try
            {
                alcCtx.LoadAdditionalAssembly(dll);
            }
            catch
            {
                // Best-effort dependency load to help DbContext discovery in sibling assemblies.
            }
        }
    }

    private static List<Assembly> BuildCompilationAssemblySet(ProjectAssemblyContext alcCtx)
    {
        var userAssemblies = alcCtx.LoadedAssemblies.ToList();
        var userNames = userAssemblies
            .Select(a => a.GetName().Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.Ordinal);

        var merged = new List<Assembly>(userAssemblies);
        merged.AddRange(
            from asm in
                AssemblyLoadContext.Default.Assemblies
            let name = asm.GetName().Name
            where string.IsNullOrWhiteSpace(name) || !userNames.Contains(name)
            select asm
        );

        return merged;
    }

    private static IReadOnlyList<Assembly> BuildExtensionDiscoveryAssemblySet(
        ProjectAssemblyContext alcCtx,
        IReadOnlyList<Assembly> compilationAssemblies)
    {
        var userAssemblies = alcCtx.LoadedAssemblies.ToList();
        var userNames = userAssemblies
            .Select(a => a.GetName().Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var discovery = new List<Assembly>(userAssemblies);
        foreach (var asm in compilationAssemblies)
        {
            var name = asm.GetName().Name;
            if (!string.IsNullOrWhiteSpace(name) && userNames.Contains(name))
                continue;

            if (IsTestOrHostAssembly(name))
                continue;

            discovery.Add(asm);
        }

        return discovery;
    }

    private static bool IsTestOrHostAssembly(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
            return true;

        return assemblyName.Contains("TestPlatform", StringComparison.OrdinalIgnoreCase)
               || assemblyName.Contains("testhost", StringComparison.OrdinalIgnoreCase)
               || assemblyName.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
               || assemblyName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase);
    }
}
