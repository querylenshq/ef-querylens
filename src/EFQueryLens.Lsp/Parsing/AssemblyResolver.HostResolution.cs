using System.Diagnostics;
using System.Text.RegularExpressions;
using EFQueryLens.Core.Scaffolding;

namespace EFQueryLens.Lsp.Parsing;

public static partial class AssemblyResolver
{
    private sealed record CandidateAssembly(
        string DllPath,
        DateTime TimestampUtc,
        bool HasFactory,
        bool HasRuntimeArtifacts,
        bool LooksLikeRefOrObj);

    /// <summary>
    /// Scans the host project's source files for an <c>IQueryLensDbContextFactory&lt;T&gt;</c>
    /// declaration and returns the concrete DbContext type name <c>T</c>.
    ///
    /// This is the authoritative way to resolve the DbContext type: the type parameter is set
    /// explicitly by the user and is always the concrete <see cref="DbContext"/> subclass —
    /// regardless of how the context is injected elsewhere (e.g. via an interface).
    ///
    /// Returns <c>null</c> when no factory declaration is found, the project directory cannot
    /// be derived from <paramref name="assemblyDllPath"/>, or any I/O error occurs.
    /// </summary>
    internal static string? TryExtractDbContextTypeFromFactory(string assemblyDllPath)
    {
        var types = TryExtractDbContextTypesFromFactory(assemblyDllPath);
        return types.Count == 1 ? types[0] : null;
    }

    internal static IReadOnlyList<string> TryExtractDbContextTypesFromFactory(string assemblyDllPath)
    {
        var projectDir = TryGetProjectDirectoryFromAssembly(assemblyDllPath);
        if (string.IsNullOrEmpty(projectDir))
        {
            return [];
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in EnumerateProjectSourceFiles(projectDir))
        {
            try
            {
                var text = File.ReadAllText(file);
                foreach (Match match in Regex.Matches(
                             text,
                             @"IQueryLensDbContextFactory\s*<\s*(?:global::)?([\w.]+)\s*>",
                             RegexOptions.None,
                             TimeSpan.FromSeconds(2)))
                {
                    var typeName = match.Groups[1].Value.Trim();
                    if (typeName.Length > 0
                        && !typeName.Equals("out", StringComparison.OrdinalIgnoreCase)
                        && !typeName.Equals("TContext", StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(typeName);
                    }
                }
            }
            catch { /* ignore unreadable files */ }
        }

        return found.OrderBy(static t => t, StringComparer.Ordinal).ToList();
    }

    private static string? TryGetProjectDirectoryFromAssembly(string assemblyDllPath)
    {
        var dir = Path.GetDirectoryName(assemblyDllPath);
        while (!string.IsNullOrEmpty(dir))
        {
            try
            {
                if (Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).Length > 0)
                {
                    return dir;
                }
            }
            catch { /* continue */ }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    /// <summary>
    /// True when the executable host project already has QueryLens factory source on disk
    /// (generated or hand-written) but the caller could not load it from the built assembly yet.
    /// </summary>
    public static bool HostProjectHasQueryLensFactorySource(string sourceFilePath)
    {
        try
        {
            var assemblyPath = TryGetTargetAssembly(sourceFilePath);
            if (string.IsNullOrWhiteSpace(assemblyPath)
                || assemblyPath.StartsWith("DEBUG_FAIL", StringComparison.Ordinal)
                || !File.Exists(assemblyPath))
            {
                return false;
            }

            var projectDir = TryGetProjectDirectoryFromAssembly(assemblyPath);
            if (string.IsNullOrEmpty(projectDir))
            {
                return false;
            }

            var generatedPath = Path.Combine(
                projectDir,
                "Properties",
                "QueryLens",
                "QueryLensDbContextFactory.g.cs");
            if (File.Exists(generatedPath))
            {
                return true;
            }

            return HasQueryLensFactory(projectDir);
        }
        catch
        {
            return false;
        }
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

        // Step 4a: Walk up to find the solution file (.slnx preferred over .sln)
        var slnFile = SolutionFileResolver.FindSolutionFile(Path.GetDirectoryName(libraryCsprojPath)!);
        if (slnFile is null)
        {
            debugLog += "  -> EXCEPTION: No .slnx or .sln file found.\n";
            return null;
        }

        debugLog += $"  -> Found solution: {Path.GetFileName(slnFile)}\n";

        // Step 4b: Parse the solution to extract project paths
        var projectEntries = SolutionFileResolver.ParseSolutionProjects(slnFile)
            .Where(p => !string.Equals(p, Path.GetFullPath(libraryCsprojPath), StringComparison.OrdinalIgnoreCase))
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

        // Step 4d: Among candidates, find one whose output contains the library DLL.
        // Prefer projects that explicitly contain a QueryLensDbContextFactory source file
        // (the user set them up as the QueryLens host) over projects that are merely
        // referencing the library for other purposes (e.g. data-migration workers).
        // Within the same tier, the most recently built DLL wins.
        var preferredTfm = TryGetLibraryPreferredTfm(libraryCsprojPath, libraryAssemblyName);
        if (preferredTfm is not null)
        {
            debugLog += $"  -> Library preferred TFM: {preferredTfm}\n";
        }

        var scored = new List<CandidateAssembly>();

        foreach (var (csprojPath, exeAssemblyName) in candidates)
        {
            var projDir = Path.GetDirectoryName(csprojPath)!;

            // Resolve all output paths for this host executable (bin glob + MSBuild fallback)
            var hostDllPaths = FindProjectOutputDllPaths(csprojPath, exeAssemblyName, ref debugLog);
            if (hostDllPaths.Count == 0)
            {
                debugLog += $"  -> {exeAssemblyName}: no output DLL found\n";
                continue;
            }

            hostDllPaths = FilterHostDllPathsByTfm(hostDllPaths, preferredTfm, ref debugLog, exeAssemblyName);
            hostDllPaths = FilterHostDllPathsByColocatedLibrary(hostDllPaths, libraryAssemblyName, ref debugLog, exeAssemblyName);
            if (hostDllPaths.Count == 0)
            {
                continue;
            }

            var hasFactory = HasQueryLensFactory(projDir);

            foreach (var hostDll in hostDllPaths)
            {
                // Verify the library DLL is co-located, proving this host was built with the reference
                var tfmDir = Path.GetDirectoryName(hostDll)!;
                var libraryDll = Path.Combine(tfmDir, $"{libraryAssemblyName}.dll");
                if (!File.Exists(libraryDll))
                {
                    debugLog += $"  -> {exeAssemblyName}: host DLL found at {hostDll} but library DLL not alongside\n";
                    continue;
                }

                var ts = File.GetLastWriteTimeUtc(hostDll);
                var hasRuntimeArtifacts = HasExecutableRuntimeArtifacts(hostDll);
                var looksLikeRefOrObj = LooksLikeRefOrObjPath(hostDll);

                debugLog +=
                    $"  -> {exeAssemblyName}: found at {hostDll} (timestamp: {ts:u}, hasFactory: {hasFactory}, " +
                    $"hasRuntimeArtifacts: {hasRuntimeArtifacts}, looksLikeRefOrObj: {looksLikeRefOrObj})\n";

                scored.Add(new CandidateAssembly(
                    hostDll,
                    ts,
                    hasFactory,
                    hasRuntimeArtifacts,
                    looksLikeRefOrObj));
            }
        }

        var bestDll = scored
            .GroupBy(x => x.DllPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(x => x.HasFactory ? 1 : 0)
                .ThenByDescending(x => x.HasRuntimeArtifacts ? 1 : 0)
                .ThenByDescending(x => x.LooksLikeRefOrObj ? 0 : 1)
                .ThenByDescending(x => x.TimestampUtc)
                .First())
            .OrderByDescending(x => x.HasFactory ? 1 : 0)
            .ThenByDescending(x => x.HasRuntimeArtifacts ? 1 : 0)
            .ThenByDescending(x => x.LooksLikeRefOrObj ? 0 : 1)
            .ThenByDescending(x => x.TimestampUtc)
            .Select(x => x.DllPath)
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
    /// Returns all candidate output DLL paths for a project, trying the bin/ folder first
    /// and falling back to an MSBuild TargetPath query for non-standard layouts such as
    /// UseArtifactsOutput=true.
    /// </summary>
    private static List<string> FindProjectOutputDllPaths(
        string csprojPath,
        string assemblyName,
        ref string debugLog)
    {
        var projectDir = Path.GetDirectoryName(csprojPath)!;
        var binDir = Path.Combine(projectDir, "bin");

        if (Directory.Exists(binDir))
        {
            var dllFiles = Directory.GetFiles(binDir, $"{assemblyName}.dll", SearchOption.AllDirectories);
            if (dllFiles.Length > 0)
            {
                debugLog += $"  -> Found {dllFiles.Length} bin candidate(s) for {assemblyName}\n";
                return [.. dllFiles];
            }

            debugLog += $"  -> {assemblyName}: not found in bin dir, trying MSBuild\n";
        }
        else
        {
            debugLog += $"  -> {assemblyName}: bin dir does not exist, trying MSBuild\n";
        }

        var msBuildDll = TryResolveDllViaMsBuild(csprojPath, ref debugLog);
        return msBuildDll is not null ? [msBuildDll] : [];
    }

    private static string? TryGetLibraryPreferredTfm(string libraryCsprojPath, string libraryAssemblyName)
    {
        var debugLog = string.Empty;
        var libraryDllPaths = FindProjectOutputDllPaths(libraryCsprojPath, libraryAssemblyName, ref debugLog);
        if (libraryDllPaths.Count == 0)
        {
            return null;
        }

        var bestLibraryDll = SelectBestDll(libraryDllPaths, ref debugLog);
        return bestLibraryDll is null ? null : TryExtractTfmFromOutputPath(bestLibraryDll);
    }

    private static List<string> FilterHostDllPathsByTfm(
        List<string> hostDllPaths,
        string? preferredTfm,
        ref string debugLog,
        string exeAssemblyName)
    {
        if (string.IsNullOrWhiteSpace(preferredTfm))
        {
            return hostDllPaths;
        }

        var tfmMatches = hostDllPaths
            .Where(path => string.Equals(
                TryExtractTfmFromOutputPath(path),
                preferredTfm,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (tfmMatches.Count > 0)
        {
            debugLog += $"  -> {exeAssemblyName}: narrowed to {tfmMatches.Count} host output(s) for TFM {preferredTfm}\n";
            return tfmMatches;
        }

        return hostDllPaths;
    }

    private static List<string> FilterHostDllPathsByColocatedLibrary(
        List<string> hostDllPaths,
        string libraryAssemblyName,
        ref string debugLog,
        string exeAssemblyName)
    {
        var colocated = hostDllPaths
            .Where(path => File.Exists(Path.Combine(Path.GetDirectoryName(path)!, $"{libraryAssemblyName}.dll")))
            .ToList();

        if (colocated.Count > 0 && colocated.Count < hostDllPaths.Count)
        {
            debugLog +=
                $"  -> {exeAssemblyName}: narrowed to {colocated.Count} host output(s) with co-located {libraryAssemblyName}.dll\n";
            return colocated;
        }

        return hostDllPaths;
    }

    private static string? TryExtractTfmFromOutputPath(string dllPath)
    {
        var parts = dllPath.Replace('\\', '/').Split('/');
        foreach (var part in parts)
        {
            if (part.Length > 3
                && part.StartsWith("net", StringComparison.OrdinalIgnoreCase)
                && char.IsDigit(part[3]))
            {
                return part;
            }
        }

        return null;
    }

    /// <summary>
    /// Picks the single best DLL from a list of candidates using runtime-artifact presence,
    /// ref/obj path detection, and last-write timestamp as tiebreakers.
    /// </summary>
    private static string? SelectBestDll(List<string> paths, ref string debugLog, string? libraryAssemblyName = null)
    {
        if (paths.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(libraryAssemblyName))
        {
            var colocated = paths
                .Where(path => File.Exists(Path.Combine(Path.GetDirectoryName(path)!, $"{libraryAssemblyName}.dll")))
                .ToList();
            if (colocated.Count > 0)
            {
                paths = colocated;
            }
        }

        var selected = paths
            .Select(path => new CandidateAssembly(
                path,
                File.GetLastWriteTimeUtc(path),
                HasFactory: false,
                HasRuntimeArtifacts: HasExecutableRuntimeArtifacts(path),
                LooksLikeRefOrObj: LooksLikeRefOrObjPath(path)))
            .OrderByDescending(x => x.HasRuntimeArtifacts ? 1 : 0)
            .ThenByDescending(x => x.LooksLikeRefOrObj ? 0 : 1)
            .ThenByDescending(x => x.TimestampUtc)
            .First();

        debugLog +=
            $"  -> Selected {selected.DllPath} " +
            $"(hasRuntimeArtifacts: {selected.HasRuntimeArtifacts}, looksLikeRefOrObj: {selected.LooksLikeRefOrObj}, timestamp: {selected.TimestampUtc:u})\n";

        return selected.DllPath;
    }

    /// <summary>
    /// Queries MSBuild for the TargetPath property of a project without triggering a build.
    /// Handles non-standard output layouts such as UseArtifactsOutput=true.
    /// </summary>
    private static string? TryResolveDllViaMsBuild(string csprojPath, ref string debugLog)
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("build");
            psi.ArgumentList.Add(csprojPath);
            psi.ArgumentList.Add("-getProperty:TargetPath");

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(15_000);

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output) && File.Exists(output))
            {
                debugLog += $"  -> MSBuild TargetPath resolved: {output}\n";
                return output;
            }

            debugLog += $"  -> MSBuild TargetPath query failed (exit: {process.ExitCode})\n";
        }
        catch (Exception ex)
        {
            debugLog += $"  -> MSBuild TargetPath exception: {ex.Message}\n";
        }

        return null;
    }

    private static bool HasExecutableRuntimeArtifacts(string dllPath)
    {
        var runtimeConfigPath = Path.ChangeExtension(dllPath, ".runtimeconfig.json");
        var depsPath = Path.ChangeExtension(dllPath, ".deps.json");
        return File.Exists(runtimeConfigPath) && File.Exists(depsPath);
    }

    private static bool LooksLikeRefOrObjPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/ref/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }
}
