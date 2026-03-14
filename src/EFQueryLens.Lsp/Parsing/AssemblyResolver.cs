using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace EFQueryLens.Lsp.Parsing;

/// <summary>
/// Resolves the target assembly (.dll) for a given C# source file.
///
/// For executable projects (Console / Web), it looks in the project's own bin folder.
/// For class library projects, it walks up to the .sln file, finds an executable project
/// that references the library, and uses its bin folder instead — because class libraries
/// don't copy NuGet dependencies to their own output directory.
/// </summary>
public static class AssemblyResolver
{
    private sealed record CachedAssemblySelection(string TargetAssemblyPath, long ExpiresAtUtcTicks);

    private static readonly ConcurrentDictionary<string, CachedAssemblySelection> TargetAssemblyCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly int TargetAssemblyCacheTtlMs = ReadIntEnvironmentVariable(
        "QUERYLENS_ASSEMBLY_RESOLVER_CACHE_TTL_MS",
        fallback: 5_000,
        min: 0,
        max: 120_000);

    /// <summary>
    /// Walks up the directory tree from the given source file path to find the nearest
    /// .csproj file, determines if it's executable or a class library, and resolves the
    /// correct assembly path accordingly.
    /// </summary>
    public static string? TryGetTargetAssembly(string sourceFilePath)
    {
        var normalizedSourceFilePath = Path.GetFullPath(sourceFilePath);
        if (TryGetCachedTargetAssembly(normalizedSourceFilePath, out var cachedTargetAssembly))
        {
            return cachedTargetAssembly;
        }

        var debugLog = $"Started at: {normalizedSourceFilePath}\n";
        var currentDir = Path.GetDirectoryName(normalizedSourceFilePath);

        // Step 1: Find the nearest .csproj
        string? csprojFile = null;
        string? projectDir = null;

        while (!string.IsNullOrEmpty(currentDir))
        {
            debugLog += $"- Checking dir: {currentDir}\n";
            var csprojFiles = Directory.GetFiles(currentDir, "*.csproj")
                .Where(f => !f.Contains("Backup", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (csprojFiles.Count > 0)
            {
                csprojFile = csprojFiles.First();
                projectDir = currentDir;
                debugLog += $"  -> Found csproj: {Path.GetFileName(csprojFile)}\n";
                break;
            }

            currentDir = Directory.GetParent(currentDir)?.FullName;
        }

        if (csprojFile is null || projectDir is null)
            return $"DEBUG_FAIL: Walked to root, no csproj found.\n{debugLog}";

        // Step 2: Extract the assembly name
        var assemblyName = Path.GetFileNameWithoutExtension(csprojFile);
        var csprojContent = File.ReadAllText(csprojFile);

        var nameMatch = Regex.Match(csprojContent, @"<AssemblyName>(.+?)</AssemblyName>");
        if (nameMatch.Success)
        {
            assemblyName = nameMatch.Groups[1].Value.Trim();
            debugLog += $"  -> AssemblyName overridden to: {assemblyName}\n";
        }

        // Step 3: Detect if this is an executable project
        if (IsExecutableProject(csprojContent))
        {
            debugLog += "  -> Project is executable, using own bin dir\n";
            var resolved = FindDllInBin(projectDir, assemblyName, ref debugLog)
                           ?? $"DEBUG_FAIL:\n{debugLog}";
            CacheTargetAssembly(normalizedSourceFilePath, resolved);
            return resolved;
        }

        // Step 4: It is a class library — find a host executable project
        debugLog += "  -> Project is a class library, searching for host executable...\n";
        var hostResolved = FindHostExecutableAssembly(csprojFile, assemblyName, ref debugLog)
                           ?? $"DEBUG_FAIL:\n{debugLog}";
        CacheTargetAssembly(normalizedSourceFilePath, hostResolved);
        return hostResolved;
    }

    /// <summary>
    /// Determines if a .csproj is an executable project (console app or web app).
    /// </summary>
    private static bool IsExecutableProject(string csprojContent)
    {
        // Web SDK projects are always executable
        if (csprojContent.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase) ||
            csprojContent.Contains("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check for explicit OutputType
        var outputTypeMatch = Regex.Match(csprojContent, @"<OutputType>(\w+)</OutputType>", RegexOptions.IgnoreCase);
        if (outputTypeMatch.Success)
        {
            var outputType = outputTypeMatch.Groups[1].Value;
            return outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) ||
                   outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Returns true if the project directory contains a source file with an
    /// IQueryLensDbContextFactory implementation — i.e. the user explicitly set
    /// this project up as the QueryLens host.
    /// </summary>
    private static bool HasQueryLensFactory(string projectDir)
    {
        foreach (var file in EnumerateProjectSourceFiles(projectDir))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Contains("QueryLensDbContextFactory", StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                var text = File.ReadAllText(file);
                if (text.Contains("IQueryLensDbContextFactory<", StringComparison.Ordinal))
                    return true;
            }
            catch
            {
                // Ignore unreadable files and continue scanning.
            }
        }

        return false;
    }

    /// <summary>
    /// Enumerates user source files for a project while skipping generated/output folders
    /// so scanning remains deterministic and resilient on large solutions.
    /// </summary>
    private static IEnumerable<string> EnumerateProjectSourceFiles(string projectDir)
    {
        var pending = new Stack<string>();
        pending.Push(projectDir);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current, "*.cs", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                files = [];
            }

            foreach (var file in files)
                yield return file;

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch
            {
                directories = [];
            }

            foreach (var dir in directories)
            {
                var name = Path.GetFileName(dir);
                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                pending.Push(dir);
            }
        }
    }

    /// <summary>
    /// Finds a host executable project that references the given class library.
    /// Strategy:
    ///   1. Walk up to find the .sln file
    ///   2. Parse the .sln to find all project paths
    ///   3. For each executable project, check if it references the class library
    ///   4. Among matching projects, prefer projects that contain a QueryLens factory
    ///      implementation; use most-recent build timestamp as a tiebreaker
    /// </summary>
    private static string? FindHostExecutableAssembly(
        string libraryCsprojPath,
        string libraryAssemblyName,
        ref string debugLog)
    {
        var libraryCsprojName = Path.GetFileName(libraryCsprojPath);

        // Step 4a: Walk up to find the .sln file
        var slnDir = Path.GetDirectoryName(libraryCsprojPath);
        string? slnFile = null;

        while (!string.IsNullOrEmpty(slnDir))
        {
            var slnFiles = Directory.GetFiles(slnDir, "*.sln");
            if (slnFiles.Length > 0)
            {
                slnFile = slnFiles.First();
                debugLog += $"  -> Found solution: {Path.GetFileName(slnFile)}\n";
                break;
            }

            slnDir = Directory.GetParent(slnDir)?.FullName;
        }

        if (slnFile is null)
        {
            debugLog += "  -> EXCEPTION: No .sln file found.\n";
            return null;
        }

        // Step 4b: Parse the .sln to extract project paths
        var slnContent = File.ReadAllText(slnFile);
        var projectEntries = Regex.Matches(slnContent,
                @"Project\("".+?""\)\s*=\s*"".+?""\s*,\s*""(.+?\.csproj)""",
                RegexOptions.Multiline)
            .Select(m => Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(slnFile)!, m.Groups[1].Value)))
            .Where(p => File.Exists(p) && !string.Equals(p,
                Path.GetFullPath(libraryCsprojPath), StringComparison.OrdinalIgnoreCase))
            .ToList();

        debugLog += $"  -> Found {projectEntries.Count} other projects in solution\n";

        // Step 4c: Find executable projects in the solution. We do not require a direct
        // ProjectReference here because many host apps reference the target library
        // transitively (e.g. UI -> Infrastructure -> Application).
        var candidates = new List<(string CsprojPath, string AssemblyName)>();

        foreach (var projPath in projectEntries)
        {
            try
            {
                var content = File.ReadAllText(projPath);

                if (!IsExecutableProject(content))
                    continue;

                var exeAssemblyName = Path.GetFileNameWithoutExtension(projPath);
                var exeNameMatch = Regex.Match(content, @"<AssemblyName>(.+?)</AssemblyName>");
                if (exeNameMatch.Success)
                    exeAssemblyName = exeNameMatch.Groups[1].Value.Trim();

                candidates.Add((projPath, exeAssemblyName));
                debugLog += $"  -> Candidate host: {Path.GetFileName(projPath)} (assembly: {exeAssemblyName})\n";
            }
            catch
            {
                // Skip unreadable projects
            }
        }

        if (candidates.Count == 0)
        {
            debugLog += "  -> EXCEPTION: No executable project references this library.\n";
            return null;
        }

        // Step 4d: Among candidates, find one whose bin folder contains the library DLL.
        // Prefer projects that explicitly contain a QueryLensDbContextFactory source file
        // (the user set them up as the QueryLens host) over projects that are merely
        // referencing the library for other purposes (e.g. data-migration workers).
        // Within the same tier, the most recently built DLL wins.
        var scored = new List<(string HostDll, DateTime Timestamp, bool HasFactory)>();

        foreach (var (csprojPath, exeAssemblyName) in candidates)
        {
            var projDir = Path.GetDirectoryName(csprojPath)!;
            var binDir = Path.Combine(projDir, "bin");

            if (!Directory.Exists(binDir))
            {
                debugLog += $"  -> {exeAssemblyName}: bin dir does not exist\n";
                continue;
            }

            // Look for the LIBRARY's DLL in the host's bin folder (proves it was built with the reference)
            var libraryDlls = Directory.GetFiles(binDir, $"{libraryAssemblyName}.dll", SearchOption.AllDirectories);
            if (libraryDlls.Length == 0)
            {
                debugLog += $"  -> {exeAssemblyName}: library DLL not found in bin\n";
                continue;
            }

            // Found it — now find the host's own DLL in the same tfm subfolder
            var libraryDll = libraryDlls.OrderByDescending(File.GetLastWriteTimeUtc).First();
            var tfmDir = Path.GetDirectoryName(libraryDll)!;

            var hostDll = Path.Combine(tfmDir, $"{exeAssemblyName}.dll");
            if (!File.Exists(hostDll))
            {
                debugLog += $"  -> {exeAssemblyName}: host DLL not found in {tfmDir}\n";
                continue;
            }

            var ts = File.GetLastWriteTimeUtc(hostDll);
            var hasFactory = HasQueryLensFactory(projDir);
            debugLog += $"  -> {exeAssemblyName}: found at {hostDll} (timestamp: {ts:u}, hasFactory: {hasFactory})\n";

            scored.Add((hostDll, ts, hasFactory));
        }

        var bestDll = scored
            .OrderByDescending(x => x.HasFactory ? 1 : 0)
            .ThenByDescending(x => x.Timestamp)
            .Select(x => x.HostDll)
            .FirstOrDefault();

        if (bestDll is not null)
        {
            debugLog += $"  -> Selected host assembly: {bestDll}\n";
        }
        else
        {
            debugLog += "  -> EXCEPTION: No candidate host project has a built bin folder containing the library.\n";
        }

        return bestDll;
    }

    /// <summary>
    /// Searches the bin directory of a project for a DLL matching the assembly name.
    /// </summary>
    private static string? FindDllInBin(string projectDir, string assemblyName, ref string debugLog)
    {
        var binDir = Path.Combine(projectDir, "bin");
        debugLog += $"  -> Checking bin dir: {binDir}\n";

        if (!Directory.Exists(binDir))
        {
            debugLog += "  -> EXCEPTION: bin directory does not exist.\n";
            return null;
        }

        var dllFiles = Directory.GetFiles(binDir, $"{assemblyName}.dll", SearchOption.AllDirectories);
        if (dllFiles.Length > 0)
        {
            return dllFiles.OrderByDescending(File.GetLastWriteTimeUtc).First();
        }

        debugLog += $"  -> EXCEPTION: Searched for {assemblyName}.dll in {binDir} recursively but found 0 files.\n";
        return null;
    }

    private static bool TryGetCachedTargetAssembly(string sourceFilePath, out string targetAssemblyPath)
    {
        targetAssemblyPath = string.Empty;

        if (TargetAssemblyCacheTtlMs <= 0)
        {
            return false;
        }

        if (!TargetAssemblyCache.TryGetValue(sourceFilePath, out var cached))
        {
            return false;
        }

        if (cached.ExpiresAtUtcTicks <= DateTime.UtcNow.Ticks || !File.Exists(cached.TargetAssemblyPath))
        {
            TargetAssemblyCache.TryRemove(sourceFilePath, out _);
            return false;
        }

        targetAssemblyPath = cached.TargetAssemblyPath;
        return true;
    }

    private static void CacheTargetAssembly(string sourceFilePath, string? resolvedAssemblyPath)
    {
        if (TargetAssemblyCacheTtlMs <= 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(resolvedAssemblyPath)
            || resolvedAssemblyPath.StartsWith("DEBUG_FAIL", StringComparison.Ordinal)
            || !File.Exists(resolvedAssemblyPath))
        {
            return;
        }

        var expires = DateTime.UtcNow.AddMilliseconds(TargetAssemblyCacheTtlMs).Ticks;
        TargetAssemblyCache[sourceFilePath] = new CachedAssemblySelection(resolvedAssemblyPath, expires);
    }

    private static int ReadIntEnvironmentVariable(string variableName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);
        if (!int.TryParse(raw, out var value))
        {
            return fallback;
        }

        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }
}
