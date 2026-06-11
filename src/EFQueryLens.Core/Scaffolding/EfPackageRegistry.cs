namespace EFQueryLens.Core.Scaffolding;

/// <summary>
/// Shared EF Core / provider package names used for deps.json-driven assembly closure.
/// </summary>
public static class EfPackageRegistry
{
    private static readonly string[] ProviderPackageNames =
    [
        "Microsoft.EntityFrameworkCore.SqlServer",
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        "Pomelo.EntityFrameworkCore.MySql",
        "Microsoft.EntityFrameworkCore.Sqlite",
    ];

    public static IReadOnlyList<string> ProviderPackageNamesList => ProviderPackageNames;

    public static bool IsEfEcosystemPackage(string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
            return false;

        if (packageName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
            return true;

        if (packageName.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var provider in ProviderPackageNames)
        {
            if (string.Equals(provider, packageName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
