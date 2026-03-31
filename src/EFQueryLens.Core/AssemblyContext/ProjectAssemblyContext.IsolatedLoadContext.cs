using System.Reflection;
using System.Runtime.Loader;
using EFQueryLens.Core.AssemblyContext.Probes;

namespace EFQueryLens.Core.AssemblyContext;

public sealed partial class ProjectAssemblyContext
{
    /// <summary>
    /// The collectible AssemblyLoadContext. Kept private and nested so callers
    /// interact only through the ProjectAssemblyContext facade.
    /// </summary>
    private sealed partial class IsolatedLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _assemblyDirectory;
        private readonly RidRuntimeProbe _ridProbe;
        private readonly AssemblyProbe[] _probes;

        /// <param name="assemblyPath">
        ///   Full path to the primary assembly. The resolver uses this to
        ///   locate the .deps.json and runtimeconfig.json produced by the
        ///   build, enabling accurate dependency resolution.
        /// </param>
        public IsolatedLoadContext(string assemblyPath)
            : base(
                name: $"QueryLens-{Path.GetFileNameWithoutExtension(assemblyPath)}",
                isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(assemblyPath);
            _assemblyDirectory = Path.GetDirectoryName(assemblyPath) ?? AppContext.BaseDirectory;
            _ridProbe = new RidRuntimeProbe(_assemblyDirectory);
            _probes =
            [
                _ridProbe,
                new LocalBinDirProbe(_assemblyDirectory),
                new DepsJsonProbe(_resolver),
                new SharedFrameworkProbe(assemblyPath),
            ];
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (ShouldPreferDefaultLoadContext(assemblyName.Name))
                return null;

            foreach (var probe in _probes)
            {
                var path = probe.TryResolve(assemblyName);
                if (!string.IsNullOrWhiteSpace(path))
                    return LoadFromAssemblyPath(path);
            }

            return null;
        }

        internal string NormalizeAssemblyPathForLoad(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return fullPath;

            try
            {
                var candidateDir = Path.GetDirectoryName(fullPath);
                if (!string.Equals(candidateDir, _assemblyDirectory, StringComparison.OrdinalIgnoreCase))
                    return fullPath;

                var assemblySimpleName = Path.GetFileNameWithoutExtension(fullPath);
                if (string.IsNullOrWhiteSpace(assemblySimpleName))
                    return fullPath;

                var ridRuntimeCandidate = _ridProbe.TryResolve(assemblySimpleName);
                if (!string.IsNullOrWhiteSpace(ridRuntimeCandidate))
                    return ridRuntimeCandidate;

                return fullPath;
            }
            catch
            {
                return fullPath;
            }
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var resolved = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return resolved is not null
                ? LoadUnmanagedDllFromPath(resolved)
                : IntPtr.Zero;
        }
    }
}
