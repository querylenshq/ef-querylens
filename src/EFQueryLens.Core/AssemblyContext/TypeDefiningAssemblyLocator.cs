using System.Reflection;

namespace EFQueryLens.Core.AssemblyContext;

// MetadataLoadContext is read-only; used only on CS0246 retry paths.

/// <summary>
/// Read-only metadata search in host bin DLLs to find which assembly defines a type (CS0246 retry).
/// </summary>
internal static class TypeDefiningAssemblyLocator
{
    public static string? TryFindAssemblySimpleName(string typeName, string hostAssemblyPath)
    {
        if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(hostAssemblyPath))
            return null;

        var binDlls = HostBinAssemblyCatalog.EnumerateHostBinDllPaths(hostAssemblyPath);
        if (binDlls.Count == 0)
            return null;

        var resolverPaths = new List<string>(binDlls);
        var corePath = typeof(object).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(corePath))
            resolverPaths.Add(corePath);

        var resolver = new PathAssemblyResolver(resolverPaths);
        var coreName = typeof(object).Assembly.GetName().Name
            ?? "System.Private.CoreLib";
        var metadataContext = new MetadataLoadContext(resolver, coreName);

        try
        {
            if (typeName.Contains('.'))
            {
                foreach (var dll in binDlls)
                {
                    try
                    {
                        var asm = metadataContext.LoadFromAssemblyPath(dll);
                        if (asm.GetType(typeName, throwOnError: false) is not null)
                            return Path.GetFileNameWithoutExtension(dll);
                    }
                    catch
                    {
                        // Best-effort per assembly.
                    }
                }

                return null;
            }

            var dottedSuffix = "." + typeName;
            foreach (var dll in binDlls)
            {
                try
                {
                    var asm = metadataContext.LoadFromAssemblyPath(dll);
                    foreach (var exported in asm.GetExportedTypes())
                    {
                        var fullName = exported.FullName?.Replace('+', '.');
                        if (fullName is not null
                            && fullName.EndsWith(dottedSuffix, StringComparison.Ordinal))
                        {
                            return Path.GetFileNameWithoutExtension(dll);
                        }
                    }
                }
                catch
                {
                    // Best-effort per assembly.
                }
            }
        }
        finally
        {
            metadataContext.Dispose();
        }

        return null;
    }
}
