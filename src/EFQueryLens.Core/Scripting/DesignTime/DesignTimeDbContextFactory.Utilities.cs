using System.Reflection;

namespace EFQueryLens.Core.Scripting;

internal static partial class DesignTimeDbContextFactory
{
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
