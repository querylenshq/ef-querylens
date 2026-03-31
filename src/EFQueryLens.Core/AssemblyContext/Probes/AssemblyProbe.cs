using System.Reflection;

namespace EFQueryLens.Core.AssemblyContext.Probes;

internal abstract class AssemblyProbe
{
    /// <summary>
    /// Attempts to resolve the full file-system path for <paramref name="assemblyName"/>.
    /// Returns <see langword="null"/> when this probe cannot satisfy the request.
    /// </summary>
    internal abstract string? TryResolve(AssemblyName assemblyName);
}
