using System.Reflection;

namespace EFQueryLens.Core.AssemblyContext.Probes;

/// <summary>
/// Resolves assemblies copied directly into the target output directory.
/// </summary>
internal sealed class LocalBinDirProbe : AssemblyProbe
{
    private readonly string _assemblyDirectory;

    internal LocalBinDirProbe(string assemblyDirectory)
    {
        _assemblyDirectory = assemblyDirectory;
    }

    internal override string? TryResolve(AssemblyName assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName.Name))
            return null;

        var candidate = Path.Combine(_assemblyDirectory, assemblyName.Name + ".dll");
        return File.Exists(candidate) ? candidate : null;
    }
}
