using System.Reflection;

namespace EFQueryLens.Core.Scripting;

/// <summary>
/// Pure-reflection helpers that discover and invoke QueryLens factory interfaces
/// in the user's assemblies, without requiring direct package references.
/// </summary>
internal static class DesignTimeDbContextFactory
{
    private const string InterfaceName =
        "Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory`1";

    private const string QueryLensInterfaceName =
        "EFQueryLens.Core.IQueryLensDbContextFactory`1";

    /// <summary>
    /// Searches <paramref name="assemblies"/> for a concrete type that implements
    /// <c>IQueryLensDbContextFactory&lt;TContext&gt;</c> — the QueryLens-native
    /// factory interface. Prioritised above <see cref="TryCreate"/> (EF Core tooling).
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

        foreach (var asm in assemblies)
        {
            Type? factoryType;
            try
            {
                factoryType = asm.GetTypes().FirstOrDefault(t =>
                    !t.IsAbstract && !t.IsInterface &&
                    t.GetInterfaces().Any(i =>
                        i.IsGenericType &&
                        i.GetGenericTypeDefinition().FullName == QueryLensInterfaceName &&
                        i.GetGenericArguments()[0].FullName == dbContextType.FullName));

                // Compatibility fallback: accept "duck-typed" factories that expose
                // a public parameterless CreateOfflineContext() returning the target
                // DbContext type, even when the generic interface identity doesn't match
                // (e.g., copied interface definition in user code).
                factoryType ??= asm.GetTypes().FirstOrDefault(t =>
                    !t.IsAbstract && !t.IsInterface &&
                    t.GetConstructor(Type.EmptyTypes) is not null &&
                    t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Any(m =>
                            m.Name == "CreateOfflineContext" &&
                            m.GetParameters().Length == 0 &&
                            m.ReturnType.FullName == dbContextType.FullName));
            }
            catch (ReflectionTypeLoadException rtle)
            {
                // Surface which dependencies were missing so callers can diagnose the failure
                // rather than silently skipping the assembly and reporting "No factory found".
                var loaderMessages = (rtle.LoaderExceptions ?? [])
                    .Where(e => e is not null)
                    .Select(e => e!.Message)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3);
                var loaderDetail = string.Join("; ", loaderMessages);
                failureReason ??= $"Could not scan '{asm.GetName().Name}' for QueryLens factory" +
                    (string.IsNullOrWhiteSpace(loaderDetail) ? "." : $": {loaderDetail}");
                continue;
            }
            catch { continue; } // Native or otherwise unscannable assemblies

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
                    && i.GetGenericTypeDefinition().FullName == QueryLensInterfaceName
                    && i.GetGenericArguments()[0].FullName == dbContextType.FullName);

                var method = matchingInterface?.GetMethod("CreateOfflineContext")
                             ?? factoryType.GetMethod("CreateOfflineContext")
                             ?? factoryType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                 .FirstOrDefault(m =>
                                     m.Name == "CreateOfflineContext"
                                     && m.GetParameters().Length == 0
                                     && m.ReturnType.FullName == dbContextType.FullName)
                             ?? factoryType.GetInterfaces()
                                 .SelectMany(i => i.GetMethods())
                                 .FirstOrDefault(m =>
                                     m.Name == "CreateOfflineContext"
                                     && m.GetParameters().Length == 0
                                     && m.ReturnType.FullName == dbContextType.FullName);

                if (method is null) continue;

                return method.Invoke(factory, null); // CreateOfflineContext takes no args
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

    /// <summary>
    /// Searches <paramref name="assemblies"/> for a concrete type that
    /// implements <c>IDesignTimeDbContextFactory&lt;TContext&gt;</c> where the
    /// generic argument's full name matches <paramref name="dbContextType"/>.
    /// Uses full-name equality so discovery works regardless of which
    /// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> the assembly was loaded into.
    /// </summary>
    /// <returns>
    /// A fresh DbContext instance returned by the factory, or <c>null</c>
    /// if no factory was found or if the factory threw during construction
    /// (callers should fall back to the bootstrap approach).
    /// </returns>
    internal static object? TryCreate(Type dbContextType, IEnumerable<Assembly> assemblies) =>
        TryCreate(dbContextType, assemblies, null, out _);

    /// <summary>
    /// Same as <see cref="TryCreate(Type, IEnumerable{Assembly})"/>, but limits
    /// discovery to factory types declared in
    /// <paramref name="requiredFactoryAssemblyPath"/> when provided.
    /// </summary>
    internal static object? TryCreate(
        Type dbContextType,
        IEnumerable<Assembly> assemblies,
        string? requiredFactoryAssemblyPath) =>
        TryCreate(dbContextType, assemblies, requiredFactoryAssemblyPath, out _);

    /// <summary>
    /// Same as <see cref="TryCreate(Type, IEnumerable{Assembly})"/>, but returns
    /// a diagnostic message when a matching design-time factory is found and
    /// invocation fails.
    /// </summary>
    internal static object? TryCreate(
        Type dbContextType,
        IEnumerable<Assembly> assemblies,
        out string? failureReason) =>
        TryCreate(dbContextType, assemblies, null, out failureReason);

    /// <summary>
    /// Same as <see cref="TryCreate(Type, IEnumerable{Assembly}, out string?)"/>,
    /// but limits discovery to factory types declared in
    /// <paramref name="requiredFactoryAssemblyPath"/> when provided.
    /// </summary>
    internal static object? TryCreate(
        Type dbContextType,
        IEnumerable<Assembly> assemblies,
        string? requiredFactoryAssemblyPath,
        out string? failureReason)
    {
        failureReason = null;
        var normalizedRequiredPath = NormalizeAssemblyPath(requiredFactoryAssemblyPath);

        foreach (var asm in assemblies)
        {
            Type? factoryType;
            try
            {
                factoryType = asm.GetTypes().FirstOrDefault(t =>
                    !t.IsAbstract && !t.IsInterface &&
                    t.GetInterfaces().Any(i =>
                        i.IsGenericType &&
                        i.GetGenericTypeDefinition().FullName == InterfaceName &&
                        i.GetGenericArguments()[0].FullName == dbContextType.FullName));
            }
            catch (ReflectionTypeLoadException rtle)
            {
                var loaderMessages = (rtle.LoaderExceptions ?? [])
                    .Where(e => e is not null)
                    .Select(e => e!.Message)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3);
                var loaderDetail = string.Join("; ", loaderMessages);
                failureReason ??= $"Could not scan '{asm.GetName().Name}' for design-time factory" +
                    (string.IsNullOrWhiteSpace(loaderDetail) ? "." : $": {loaderDetail}");
                continue;
            }
            catch { continue; }

            if (factoryType is null) continue;

            if (!IsFactoryAssemblyAllowed(
                    factoryType,
                    normalizedRequiredPath,
                    "design-time",
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
                    && i.GetGenericTypeDefinition().FullName == InterfaceName
                    && i.GetGenericArguments()[0].FullName == dbContextType.FullName);

                var method = matchingInterface?.GetMethod("CreateDbContext")
                             ?? factoryType.GetMethod("CreateDbContext")
                             ?? factoryType.GetInterfaces()
                                 .SelectMany(i => i.GetMethods())
                                 .FirstOrDefault(m =>
                                     m.Name == "CreateDbContext"
                                     && m.GetParameters().Length == 1);

                if (method is null) continue;

                return method.Invoke(factory, [Array.Empty<string>()]);
            }
            catch (Exception ex)
            {
                failureReason =
                    $"Found design-time factory '{factoryType.FullName}' but CreateDbContext(string[]) failed: {Unwrap(ex)}";
                return null;
            }
        }

        return null;
    }

    private static bool IsFactoryAssemblyAllowed(
        Type factoryType,
        string? normalizedRequiredPath,
        string factoryKind,
        out string mismatchReason)
    {
        mismatchReason = string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedRequiredPath))
            return true;

        var factoryAssemblyPath = NormalizeAssemblyPath(factoryType.Assembly.Location);
        if (string.IsNullOrWhiteSpace(factoryAssemblyPath)
            || string.Equals(factoryAssemblyPath, normalizedRequiredPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        mismatchReason =
            $"Found {factoryKind} factory '{factoryType.FullName}' in '{Path.GetFileName(factoryAssemblyPath)}', " +
            $"but QueryLens requires factories in the selected executable assembly '{Path.GetFileName(normalizedRequiredPath)}'.";
        return false;
    }

    private static string? NormalizeAssemblyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static string Unwrap(Exception ex)
    {
        var current = ex;
        while (current is TargetInvocationException tie && tie.InnerException is not null)
        {
            current = tie.InnerException;
        }

        return current.Message;
    }
}
