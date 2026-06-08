using System.Text.RegularExpressions;

namespace EFQueryLens.Core.Scaffolding;

/// <summary>
/// Resolves executable host projects and built assembly paths for CLI and headless setup flows.
/// </summary>
public static class SetupHostResolver
{
    public sealed record ResolvedHost(
        string CsprojPath,
        string ProjectDirectory,
        string? AssemblyPath,
        string DisplayName);

    public static IReadOnlyList<ResolvedHost> ListExecutableHosts(string solutionOrProjectPath)
    {
        var normalized = Path.GetFullPath(solutionOrProjectPath);
        if (normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return SolutionFileResolver.ParseSolutionProjects(normalized)
                .Select(ResolveHostFromCsproj)
                .Where(host => host is not null)
                .Select(host => host!)
                .ToList();
        }

        var csproj = ResolveCsprojPath(normalized);
        if (csproj is null)
        {
            return [];
        }

        var host = ResolveHostFromCsproj(csproj);
        return host is null ? [] : [host];
    }

    public static ResolvedHost? ResolveHost(string projectPath, string? hostCsprojPath = null)
    {
        if (!string.IsNullOrWhiteSpace(hostCsprojPath))
        {
            var hostPath = Path.GetFullPath(hostCsprojPath);
            if (!File.Exists(hostPath))
            {
                return null;
            }

            return ResolveHostFromCsproj(hostPath);
        }

        var normalized = Path.GetFullPath(projectPath);
        if (normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var csproj = ResolveCsprojPath(normalized);
        if (csproj is null)
        {
            return null;
        }

        var content = File.ReadAllText(csproj);
        if (IsExecutableProject(content))
        {
            return ResolveHostFromCsproj(csproj);
        }

        var slnFile = SolutionFileResolver.FindSolutionFile(Path.GetDirectoryName(csproj)!);
        if (slnFile is null)
        {
            return null;
        }

        var libraryAssemblyName = ResolveAssemblyName(csproj, content);
        var hosts = SolutionFileResolver.ParseSolutionProjects(slnFile)
            .Select(ResolveHostFromCsproj)
            .Where(host => host is not null && host.AssemblyPath is not null)
            .Select(host => host!)
            .Where(host => HostReferencesLibrary(host, libraryAssemblyName))
            .ToList();

        return hosts.Count switch
        {
            0 => null,
            1 => hosts[0],
            _ => null,
        };
    }

    public static ProviderKind ParseProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ProviderKind.Unknown;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "sqlserver" => ProviderKind.SqlServer,
            "npgsql" or "postgres" or "postgresql" => ProviderKind.Npgsql,
            "mysql" => ProviderKind.MySql,
            "sqlite" => ProviderKind.Sqlite,
            _ => ProviderKind.Unknown,
        };
    }

    private static ResolvedHost? ResolveHostFromCsproj(string csprojPath)
    {
        var fullPath = Path.GetFullPath(csprojPath);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var content = File.ReadAllText(fullPath);
        if (!IsExecutableProject(content))
        {
            return null;
        }

        var assemblyName = ResolveAssemblyName(fullPath, content);
        var assemblyPath = FindBuiltAssembly(fullPath, assemblyName);

        return new ResolvedHost(
            fullPath,
            Path.GetDirectoryName(fullPath)!,
            assemblyPath,
            Path.GetFileNameWithoutExtension(fullPath));
    }

    private static bool HostReferencesLibrary(ResolvedHost host, string libraryAssemblyName)
    {
        if (host.AssemblyPath is null)
        {
            return false;
        }

        var tfmDir = Path.GetDirectoryName(host.AssemblyPath)!;
        return File.Exists(Path.Combine(tfmDir, $"{libraryAssemblyName}.dll"));
    }

    private static string? ResolveCsprojPath(string path)
    {
        if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return File.Exists(path) ? path : null;
        }

        if (!Directory.Exists(path))
        {
            return null;
        }

        return Directory.GetFiles(path, "*.csproj")
            .FirstOrDefault(file => !file.Contains("Backup", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveAssemblyName(string csprojPath, string csprojContent)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(csprojPath);
        var nameMatch = Regex.Match(csprojContent, @"<AssemblyName>(.+?)</AssemblyName>");
        if (nameMatch.Success)
        {
            assemblyName = nameMatch.Groups[1].Value.Trim();
        }

        return assemblyName;
    }

    internal static bool IsExecutableProject(string csprojContent)
    {
        if (csprojContent.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)
            || csprojContent.Contains("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var outputTypeMatch = Regex.Match(csprojContent, @"<OutputType>(\w+)</OutputType>", RegexOptions.IgnoreCase);
        if (outputTypeMatch.Success)
        {
            var outputType = outputTypeMatch.Groups[1].Value;
            return outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase)
                   || outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string? FindBuiltAssembly(string csprojPath, string assemblyName)
    {
        var projectDir = Path.GetDirectoryName(csprojPath)!;
        var binDir = Path.Combine(projectDir, "bin");
        if (!Directory.Exists(binDir))
        {
            return null;
        }

        var dllFiles = Directory.GetFiles(binDir, $"{assemblyName}.dll", SearchOption.AllDirectories);
        if (dllFiles.Length == 0)
        {
            return null;
        }

        return dllFiles
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => info.FullName)
            .FirstOrDefault();
    }

}
