using System.Reflection;

namespace EFQueryLens.Core.AssemblyContext;

public sealed partial class ProjectAssemblyContext
{
    private readonly HashSet<string> _closureAssemblyNames = new(StringComparer.OrdinalIgnoreCase);
    private string? _loadedClosureKey;
    private readonly object _closureLock = new();

    /// <summary>Assembly simple names in the EF-domain closure (host + project refs + EF packages).</summary>
    public IReadOnlySet<string> ClosureAssemblyNames => _closureAssemblyNames;

    /// <summary>Number of assemblies loaded into the isolated ALC after staged loading.</summary>
    public int LoadedAssemblyCount
    {
        get
        {
            EnsureNotDisposed();
            return _ctx?.Assemblies.Count() ?? 0;
        }
    }

    /// <summary>
    /// Returns true when <paramref name="assembly"/> is part of the EF-domain closure.
    /// </summary>
    public bool IsClosureAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        return !string.IsNullOrWhiteSpace(name) && _closureAssemblyNames.Contains(name);
    }

    /// <summary>
    /// Loads or extends the EF-domain closure. Safe to call repeatedly per translate request.
    /// </summary>
    public void EnsureDomainClosureLoaded(string? dbContextTypeName = null)
    {
        EnsureNotDisposed();
        if (_ctx is null)
            return;

        var closureKey = dbContextTypeName ?? string.Empty;
        lock (_closureLock)
        {
            if (string.Equals(_loadedClosureKey, closureKey, StringComparison.Ordinal)
                && _closureAssemblyNames.Count > 0)
            {
                return;
            }

            if (!EfDomainClosureLoader.TryBuildClosure(AssemblyPath, dbContextTypeName, out var closure))
            {
                _closureAssemblyNames.Clear();
                _closureAssemblyNames.Add(Path.GetFileNameWithoutExtension(AssemblyPath));
            }
            else
            {
                _closureAssemblyNames.Clear();
                foreach (var name in closure.AssemblySimpleNames)
                {
                    _closureAssemblyNames.Add(name);
                }
            }

            LoadClosureAssemblies();
            _loadedClosureKey = closureKey;
        }
    }

    /// <summary>
    /// Stage 1: load assemblies in the EF-domain closure from deps.json.
    /// </summary>
    private void LoadDomainClosure()
    {
        EnsureDomainClosureLoaded();
    }

    /// <summary>
    /// Stage 2: load remaining closure DLLs present in bin but not yet loaded.
    /// </summary>
    public void LoadRemainingBinAssemblies()
    {
        var dir = Path.GetDirectoryName(AssemblyPath);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return;

        var loaded = LoadedAssemblies
            .Select(a => a.Location)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var simpleName in _closureAssemblyNames)
        {
            if (ShouldPreferDefaultLoadContext(simpleName))
                continue;

            var dll = Path.Combine(dir, simpleName + ".dll");
            if (!File.Exists(dll) || loaded.Contains(dll))
                continue;

            try
            {
                LoadAdditionalAssembly(dll);
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    /// <summary>
    /// Generic on-demand load: adds <paramref name="assemblySimpleName"/> to the closure
    /// and loads it when resolvable from deps.json / bin.
    /// </summary>
    public bool TryLoadOnDemandAssembly(string assemblySimpleName)
    {
        EnsureNotDisposed();
        if (_ctx is null || string.IsNullOrWhiteSpace(assemblySimpleName))
            return false;

        if (IsAssemblyLoaded(assemblySimpleName))
            return true;

        lock (_closureLock)
        {
            _closureAssemblyNames.Add(assemblySimpleName);
            return _ctx.TryLoadReferencedAssembly(new AssemblyName(assemblySimpleName), out _);
        }
    }

    /// <summary>
    /// Attempts to load project-reference assemblies from bin that are not yet loaded.
    /// </summary>
    public bool TryLoadUnloadedClosureAssembliesFromBin()
    {
        var before = LoadedAssemblyCount;
        LoadRemainingBinAssemblies();
        return LoadedAssemblyCount > before;
    }

    /// <summary>
    /// Attempts to load any deps.json runtime assembly present in host bin but not yet in the ALC.
    /// </summary>
    public bool TryLoadUnloadedDepsAssembliesFromBin()
    {
        if (!DepsJsonAssemblyIndex.TryGetAllRuntimeAssemblySimpleNames(AssemblyPath, out var names))
            return false;

        var before = LoadedAssemblyCount;
        foreach (var simpleName in names)
        {
            TryLoadOnDemandAssembly(simpleName);
        }

        return LoadedAssemblyCount > before;
    }

    /// <summary>
    /// Locates and loads the assembly that defines <paramref name="typeName"/> from host bin metadata.
    /// </summary>
    public bool TryLoadAssemblyDefiningType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        var definingAssembly = TypeDefiningAssemblyLocator.TryFindAssemblySimpleName(
            typeName,
            AssemblyPath);
        if (string.IsNullOrWhiteSpace(definingAssembly))
            return false;

        return TryLoadOnDemandAssembly(definingAssembly);
    }

    private void LoadClosureAssemblies()
    {
        if (_ctx is null)
            return;

        foreach (var assemblyName in EfDomainClosureLoader.ToAssemblyNames(_closureAssemblyNames))
        {
            if (ShouldPreferDefaultLoadContext(assemblyName.Name))
                continue;

            _ctx.TryLoadReferencedAssembly(assemblyName, out _);
        }

        LoadRemainingBinAssemblies();
    }

    private bool IsAssemblyLoaded(string assemblySimpleName) =>
        LoadedAssemblies.Any(a =>
            string.Equals(a.GetName().Name, assemblySimpleName, StringComparison.OrdinalIgnoreCase));
}
