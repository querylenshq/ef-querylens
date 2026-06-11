using System.Reflection;
using System.Runtime.Loader;
using EFQueryLens.Core.AssemblyContext;

namespace EFQueryLens.Core.Scripting.Evaluation;

public sealed partial class QueryEvaluator
{
    internal static bool IsNoDbContextFoundError(InvalidOperationException ex) =>
        ex is DbContextDiscoveryException { FailureKind: DbContextDiscoveryFailureKind.NoDbContextFound };

    internal static void TryLoadSiblingAssemblies(ProjectAssemblyContext alcCtx) =>
        alcCtx.LoadRemainingBinAssemblies();

    private static List<Assembly> GetClosureAssemblies(ProjectAssemblyContext alcCtx) =>
        alcCtx.LoadedAssemblies.Where(alcCtx.IsClosureAssembly).ToList();

    private static bool TryResolveMissingTypeOnDemand(
        ProjectAssemblyContext alcCtx,
        string typeName,
        ref HashSet<string> knownNamespaces,
        ref HashSet<string> knownTypes)
    {
        var changed = alcCtx.TryLoadUnloadedDepsAssembliesFromBin();

        if (!string.IsNullOrWhiteSpace(typeName)
            && alcCtx.TryLoadAssemblyDefiningType(typeName))
        {
            changed = true;
        }

        if (!changed)
            return false;

        var rebuilt = BuildKnownNamespaceAndTypeIndex(alcCtx.LoadedAssemblies);
        knownNamespaces = rebuilt.Namespaces;
        knownTypes = rebuilt.Types;
        return true;
    }

    private static List<Assembly> BuildCompilationAssemblySet(ProjectAssemblyContext alcCtx)
    {
        // Roslyn needs every assembly actually loaded in the user ALC (especially EF Core
        // and provider packages). Closure filtering applies to type scans only — not metadata refs.
        alcCtx.TryLoadUnloadedClosureAssembliesFromBin();
        var userAssemblies = alcCtx.LoadedAssemblies.ToList();
        var userNames = userAssemblies
            .Select(a => a.GetName().Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.Ordinal);

        var hostBinNames = HostBinAssemblyCatalog.GetHostBinAssemblySimpleNames(alcCtx.AssemblyPath);
        var merged = new List<Assembly>(userAssemblies);
        merged.AddRange(
            from asm in AssemblyLoadContext.Default.Assemblies
            let name = asm.GetName().Name
            where string.IsNullOrWhiteSpace(name)
                  || (!userNames.Contains(name)
                      && (name is null || !hostBinNames.Contains(name)))
            select asm
        );

        return merged;
    }

}
