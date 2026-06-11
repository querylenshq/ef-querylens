using System.Reflection;
using EFQueryLens.Core.AssemblyContext;

namespace EFQueryLens.Core.Scripting.DesignTime;

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

        if (IsColocatedQueryLensHostAssembly(factoryAssemblyPath, normalizedRequiredPath)
            || IsShadowBundleEquivalent(factoryAssemblyPath, normalizedRequiredPath))
        {
            return true;
        }

        mismatchReason =
            $"Found {factoryKind} factory '{factoryType.FullName}' in '{Path.GetFileName(factoryAssemblyPath)}', " +
            $"but QueryLens requires factories in the selected executable assembly '{Path.GetFileName(normalizedRequiredPath)}'.";
        return false;
    }

    private static bool IsShadowBundleEquivalent(
        string factoryAssemblyPath,
        string normalizedRequiredPath)
    {
        if (!string.Equals(
                Path.GetFileName(factoryAssemblyPath),
                Path.GetFileName(normalizedRequiredPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var marker = $"{Path.DirectorySeparatorChar}EFQueryLens{Path.DirectorySeparatorChar}shadow{Path.DirectorySeparatorChar}";
        return factoryAssemblyPath.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsColocatedQueryLensHostAssembly(
        string factoryAssemblyPath,
        string normalizedRequiredPath)
    {
        var factoryDir = Path.GetDirectoryName(factoryAssemblyPath);
        var requiredDir = Path.GetDirectoryName(normalizedRequiredPath);
        if (string.IsNullOrWhiteSpace(factoryDir)
            || string.IsNullOrWhiteSpace(requiredDir)
            || !string.Equals(factoryDir, requiredDir, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var factoryName = Path.GetFileNameWithoutExtension(factoryAssemblyPath);
        return factoryName.Contains("Migrations", StringComparison.OrdinalIgnoreCase)
               || factoryName.Contains("QueryLens", StringComparison.OrdinalIgnoreCase)
               || factoryName.Contains("EFCoreMigrations", StringComparison.OrdinalIgnoreCase);
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

    private static IReadOnlyList<Type> GetLoadableTypes(Assembly assembly, ref string? failureReason)
    {
        string? localFailure = failureReason;
        var types = AssemblyReflection.GetCachedLoadableTypes(
            assembly,
            new AssemblyReflection.ScanOptions
            {
                OnDiagnostic = message => TryRecordFactoryScanFailureMessage(message, ref localFailure),
            });
        failureReason = localFailure;
        return types;
    }

    private static void TryRecordFactoryScanFailureMessage(string message, ref string? failureReason) =>
        failureReason ??= message;

    private static bool IsQueryLensFactoryInterface(Type genericTypeDefinition)
    {
        var fullName = genericTypeDefinition.FullName;
        return string.Equals(fullName, QueryLensInterfaceName, StringComparison.Ordinal)
               || string.Equals(fullName, GeneratedQueryLensInterfaceName, StringComparison.Ordinal);
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
