using System.Reflection;
using EFQueryLens.Core.AssemblyContext;

namespace EFQueryLens.Core.Scripting.DesignTime;

internal static partial class DesignTimeDbContextFactory
{
    /// <summary>
    /// Searches <paramref name="assemblies"/> for a concrete type that implements
    /// <c>IQueryLensDbContextFactory&lt;TContext&gt;</c> — the QueryLens-native
    /// factory interface.
    /// </summary>
    /// <returns>
    /// A fresh DbContext instance returned by <c>CreateOfflineContext()</c>,
    /// or <c>null</c> if no factory was found or construction failed.
    /// </returns>
    internal static object? TryCreateQueryLensFactory(
        Type dbContextType, IEnumerable<Assembly> assemblies) =>
        TryCreateQueryLensFactory(dbContextType, assemblies, null, out _);

    /// <summary>
    /// Same as <see cref="TryCreateQueryLensFactory(Type, IEnumerable{Assembly})"/>,
    /// but limits discovery to factory types declared in
    /// <paramref name="requiredFactoryAssemblyPath"/> when provided.
    /// </summary>
    internal static object? TryCreateQueryLensFactory(
        Type dbContextType,
        IEnumerable<Assembly> assemblies,
        string? requiredFactoryAssemblyPath) =>
        TryCreateQueryLensFactory(dbContextType, assemblies, requiredFactoryAssemblyPath, out _);

    /// <summary>
    /// Same as <see cref="TryCreateQueryLensFactory(Type, IEnumerable{Assembly})"/>,
    /// but returns a diagnostic message when a matching factory type is found and
    /// invocation fails.
    /// </summary>
    internal static object? TryCreateQueryLensFactory(
        Type dbContextType,
        IEnumerable<Assembly> assemblies,
        out string? failureReason) =>
        TryCreateQueryLensFactory(dbContextType, assemblies, null, out failureReason);

    /// <summary>
    /// Same as <see cref="TryCreateQueryLensFactory(Type, IEnumerable{Assembly}, out string?)"/>,
    /// but limits discovery to factory types declared in
    /// <paramref name="requiredFactoryAssemblyPath"/> when provided.
    /// </summary>
    internal static object? TryCreateQueryLensFactory(
        Type dbContextType,
        IEnumerable<Assembly> assemblies,
        string? requiredFactoryAssemblyPath,
        out string? failureReason)
    {
        failureReason = null;
        var normalizedRequiredPath = NormalizeAssemblyPath(requiredFactoryAssemblyPath);
        var discoveryAssemblies = SelectFactoryDiscoveryAssemblies(assemblies, normalizedRequiredPath).ToList();
        if (!string.IsNullOrWhiteSpace(normalizedRequiredPath) && discoveryAssemblies.Count == 0)
        {
            failureReason =
                $"No QueryLens factory found in required executable assembly '{Path.GetFileName(normalizedRequiredPath)}'.";
            return null;
        }

        foreach (var asm in discoveryAssemblies)
        {
            Type? factoryType;
            try
            {
                factoryType = TryFindFactoryTypeInAssembly(asm, dbContextType, ref failureReason);
            }
            catch (Exception ex) when (AssemblyReflection.IsIgnorableReflectionFailure(ex))
            {
                if (!TryRecordFactoryScanFailure(asm, [ex], ref failureReason))
                {
                    continue;
                }

                continue;
            }
            catch { continue; }

            if (factoryType is null) continue;

            if (!IsFactoryAssemblyAllowed(
                    factoryType,
                    normalizedRequiredPath,
                    "QueryLens",
                    out var locationMismatch))
            {
                failureReason ??= locationMismatch;
                continue;
            }

            try
            {
                var factory = Activator.CreateInstance(factoryType)!;
                var matchingInterface = factoryType.GetInterfaces().FirstOrDefault(i =>
                    i.IsGenericType
                    && IsQueryLensFactoryInterface(i.GetGenericTypeDefinition())
                    && i.GetGenericArguments()[0].FullName == dbContextType.FullName);

                var method = matchingInterface?.GetMethod("CreateOfflineContext")
                             ?? factoryType.GetMethod("CreateOfflineContext");

                if (method is null) continue;

                return method.Invoke(factory, null);
            }
            catch (Exception ex)
            {
                failureReason =
                    $"Found QueryLens factory '{factoryType.FullName}' but CreateOfflineContext() failed: {Unwrap(ex)}";
                return null;
            }
        }

        return null;
    }

    private static IEnumerable<Assembly> SelectFactoryDiscoveryAssemblies(
        IEnumerable<Assembly> assemblies,
        string? normalizedRequiredPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedRequiredPath))
            return assemblies;

        return assemblies.Where(asm =>
        {
            var location = NormalizeAssemblyPath(asm.Location);
            return !string.IsNullOrWhiteSpace(location)
                && string.Equals(location, normalizedRequiredPath, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static Type? TryFindFactoryTypeInAssembly(Assembly asm, Type dbContextType, ref string? failureReason)
    {
        foreach (var t in GetLoadableTypes(asm, ref failureReason))
        {
            try
            {
                if (t.IsAbstract || t.IsInterface)
                    continue;

                if (ImplementsQueryLensFactoryInterface(t, dbContextType))
                    return t;
            }
            catch (Exception ex) when (AssemblyReflection.IsIgnorableReflectionFailure(ex))
            {
                continue;
            }
        }

        return null;
    }

    private static bool ImplementsQueryLensFactoryInterface(Type type, Type dbContextType)
    {
        foreach (var iface in type.GetInterfaces())
        {
            if (!iface.IsGenericType)
                continue;

            if (!IsQueryLensFactoryInterface(iface.GetGenericTypeDefinition()))
                continue;

            if (iface.GetGenericArguments()[0].FullName == dbContextType.FullName)
                return true;
        }

        return false;
    }

    private static bool TryRecordFactoryScanFailure(
        Assembly asm,
        Exception?[]? loaderExceptions,
        ref string? failureReason)
    {
        var loaderMessages = (loaderExceptions ?? [])
            .Where(e => e is not null && !AssemblyReflection.IsIgnorableReflectionFailure(e!))
            .Select(e => e!.Message)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (loaderMessages.Count == 0)
        {
            return false;
        }

        var loaderDetail = string.Join("; ", loaderMessages);
        failureReason ??= $"Could not scan '{asm.GetName().Name}' for QueryLens factory: {loaderDetail}";
        return true;
    }
}
