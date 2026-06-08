using System.Text.RegularExpressions;
using EFQueryLens.Core.Scaffolding;

namespace EFQueryLens.Lsp.Parsing;

public static partial class AssemblyResolver
{
    public static SetupDetectResult DetectSetupHosts(string sourceFilePath)
    {
        var normalizedSourceFilePath = Path.GetFullPath(sourceFilePath);
        var projectDir = TryGetProjectDirectory(normalizedSourceFilePath);
        if (string.IsNullOrWhiteSpace(projectDir))
        {
            return new SetupDetectResult
            {
                Message = "Could not locate a .csproj for the current file.",
            };
        }

        var csprojFile = Directory.GetFiles(projectDir, "*.csproj")
            .FirstOrDefault(f => !f.Contains("Backup", StringComparison.OrdinalIgnoreCase));
        if (csprojFile is null)
        {
            return new SetupDetectResult
            {
                Message = "Could not locate a .csproj for the current file.",
            };
        }

        var csprojContent = File.ReadAllText(csprojFile);
        if (IsExecutableProject(csprojContent))
        {
            var assemblyName = ResolveAssemblyName(csprojFile, csprojContent);
            var debugLog = string.Empty;
            var dllPaths = FindProjectOutputDllPaths(csprojFile, assemblyName, ref debugLog);
            var assemblyPath = SelectBestDll(dllPaths, ref debugLog);

            var host = new SetupHostCandidate
            {
                ProjectPath = csprojFile,
                DisplayName = Path.GetFileNameWithoutExtension(csprojFile),
                AssemblyPath = assemblyPath,
                ProjectDirectory = projectDir,
                IsDefault = true,
            };

            return new SetupDetectResult
            {
                RequiresHostSelection = false,
                DefaultHostProjectPath = csprojFile,
                Hosts = [host],
                Message = assemblyPath is null
                    ? "Build the executable host project, then run Set up QueryLens again."
                    : null,
            };
        }

        var libraryAssemblyName = ResolveAssemblyName(csprojFile, csprojContent);
        var hostDebugLog = string.Empty;
        var hosts = EnumerateLibraryHostCandidates(csprojFile, libraryAssemblyName, ref hostDebugLog)
            .Select(candidate => new SetupHostCandidate
            {
                ProjectPath = candidate.CsprojPath,
                DisplayName = Path.GetFileNameWithoutExtension(candidate.CsprojPath),
                AssemblyPath = candidate.AssemblyPath,
                ProjectDirectory = Path.GetDirectoryName(candidate.CsprojPath),
                IsDefault = candidate.IsDefault,
            })
            .ToList();

        if (hosts.Count == 0)
        {
            return new SetupDetectResult
            {
                Message = "No built executable host was found for this class library. Build the solution, then try again.",
            };
        }

        var defaultHost = hosts.FirstOrDefault(h => h.IsDefault) ?? hosts[0];
        return new SetupDetectResult
        {
            RequiresHostSelection = hosts.Count > 1,
            DefaultHostProjectPath = defaultHost.ProjectPath,
            Hosts = hosts,
        };
    }

    public static SetupHostCandidate? ResolveSetupHost(string? hostProjectPath, string sourceFilePath)
    {
        if (!string.IsNullOrWhiteSpace(hostProjectPath) && File.Exists(hostProjectPath))
        {
            var projectDir = Path.GetDirectoryName(Path.GetFullPath(hostProjectPath))!;
            var content = File.ReadAllText(hostProjectPath);
            var assemblyName = ResolveAssemblyName(hostProjectPath, content);
            var debugLog = string.Empty;
            var dllPaths = FindProjectOutputDllPaths(hostProjectPath, assemblyName, ref debugLog);
            var assemblyPath = SelectBestDll(dllPaths, ref debugLog);

            return new SetupHostCandidate
            {
                ProjectPath = hostProjectPath,
                DisplayName = Path.GetFileNameWithoutExtension(hostProjectPath),
                AssemblyPath = assemblyPath,
                ProjectDirectory = projectDir,
                IsDefault = true,
            };
        }

        var detect = DetectSetupHosts(sourceFilePath);
        return detect.Hosts.FirstOrDefault(h => h.IsDefault)
               ?? detect.Hosts.FirstOrDefault();
    }

    private sealed record LibraryHostCandidate(
        string CsprojPath,
        string? AssemblyPath,
        bool IsDefault);

    private static List<LibraryHostCandidate> EnumerateLibraryHostCandidates(
        string libraryCsprojPath,
        string libraryAssemblyName,
        ref string debugLog)
    {
        var slnFile = SolutionFileResolver.FindSolutionFile(Path.GetDirectoryName(libraryCsprojPath)!);
        if (slnFile is null)
        {
            debugLog += "No .slnx or .sln file found while enumerating setup hosts.\n";
            return [];
        }

        var projectEntries = SolutionFileResolver.ParseSolutionProjects(slnFile)
            .Where(p => !string.Equals(p, Path.GetFullPath(libraryCsprojPath), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var preferredTfm = TryGetLibraryPreferredTfm(libraryCsprojPath, libraryAssemblyName);
        var scoredHosts = new List<(string CsprojPath, string AssemblyPath, bool HasFactory, DateTime Timestamp)>();

        foreach (var projPath in projectEntries)
        {
            try
            {
                var content = File.ReadAllText(projPath);
                if (!IsExecutableProject(content))
                {
                    continue;
                }

                var exeAssemblyName = ResolveAssemblyName(projPath, content);
                var projDir = Path.GetDirectoryName(projPath)!;
                var hasFactory = HasQueryLensFactory(projDir);
                var hostDllPaths = FindProjectOutputDllPaths(projPath, exeAssemblyName, ref debugLog);
                hostDllPaths = FilterHostDllPathsByTfm(hostDllPaths, preferredTfm, ref debugLog, exeAssemblyName);
                hostDllPaths = FilterHostDllPathsByColocatedLibrary(hostDllPaths, libraryAssemblyName, ref debugLog, exeAssemblyName);

                foreach (var hostDll in hostDllPaths)
                {
                    var tfmDir = Path.GetDirectoryName(hostDll)!;
                    var libraryDll = Path.Combine(tfmDir, $"{libraryAssemblyName}.dll");
                    if (!File.Exists(libraryDll))
                    {
                        continue;
                    }

                    scoredHosts.Add((projPath, hostDll, hasFactory, File.GetLastWriteTimeUtc(hostDll)));
                }
            }
            catch
            {
                // Skip unreadable projects.
            }
        }

        var ordered = scoredHosts
            .GroupBy(x => x.CsprojPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(x => x.HasFactory ? 1 : 0)
                .ThenByDescending(x => x.Timestamp)
                .First())
            .OrderByDescending(x => x.HasFactory ? 1 : 0)
            .ThenByDescending(x => x.Timestamp)
            .ToList();

        if (ordered.Count == 0)
        {
            return [];
        }

        var defaultCsproj = ordered[0].CsprojPath;
        return ordered
            .Select(host => new LibraryHostCandidate(
                host.CsprojPath,
                host.AssemblyPath,
                string.Equals(host.CsprojPath, defaultCsproj, StringComparison.OrdinalIgnoreCase)))
            .ToList();
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
}
