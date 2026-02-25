#!/usr/bin/env dotnet-script
// prep-prod-rel.csx - Automate prod-rel branch preparation
// Usage: dotnet script scripts/prep-prod-rel.csx [--dry-run] [--allow-non-prod-rel]
//
// This script:
//   1. Verifies current branch is deploy/prod-rel-*
//   2. Fetches origin/deploy/prod for comparison
//   3. Detects changed packable projects (git diff + IsPackable check)
//   4. Auto-bumps MINOR version for changed projects
//   5. Updates Directory.Packages.Prod.props with new versions
//   6. Runs validation checks (same as prod-rel-validate.csx)
//   7. Shows summary and prompts for confirmation
//   8. Stages files if confirmed
//
// Exit codes:
//   0 = Success (changes applied or dry-run)
//   1 = Validation failed or user cancelled

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// --- Configuration ---
var dryRun = Args.Contains("--dry-run");
var allowNonProdRel = Args.Contains("--allow-non-prod-rel");
var repoRoot = Environment.CurrentDirectory;
var packagesPropsPath = Path.Combine(repoRoot, "Directory.Packages.props");
var prodPropsPath = Path.Combine(repoRoot, "Directory.Packages.Prod.props");
var srcDir = Path.Combine(repoRoot, "src");

// --- Color helpers ---
void PrintError(string message) => Console.WriteLine($"\u001b[31m❌ {message}\u001b[0m");
void PrintSuccess(string message) => Console.WriteLine($"\u001b[32m✅ {message}\u001b[0m");
void PrintWarning(string message) => Console.WriteLine($"\u001b[33m⚠️  {message}\u001b[0m");
void PrintInfo(string message) => Console.WriteLine($"\u001b[36mℹ️  {message}\u001b[0m");
void PrintStep(string message) => Console.WriteLine($"\u001b[1m=== {message} ===\u001b[0m");

// --- Git helpers ---
string RunGit(string args)
{
    var psi = new ProcessStartInfo
    {
        FileName = "git",
        Arguments = args,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    
    using var process = Process.Start(psi);
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    
    if (process.ExitCode != 0)
    {
        throw new Exception($"Git command failed: git {args}\n{error}");
    }
    
    return output.Trim();
}

// --- XML helpers ---
XNamespace GetMsBuildNamespace(XDocument doc)
{
    return doc.Root?.Name.Namespace ?? XNamespace.None;
}

string GetMinorVersion(string versionPrefix)
{
    var match = Regex.Match(versionPrefix, @"^(\d+)\.(\d+)");
    if (!match.Success)
    {
        throw new Exception($"Invalid VersionPrefix format: {versionPrefix}");
    }
    return match.Groups[2].Value;
}

// --- Step 1: Verify branch ---
PrintStep("Step 1: Verify branch");
var currentBranch = RunGit("rev-parse --abbrev-ref HEAD");
var prodRelPattern = @"^(deploy/(test-)?prod-rel-\d{8}(-hotfix-.*)?|test/prod-rel-\d{8})$|^deploy/prod$";

if (!Regex.IsMatch(currentBranch, prodRelPattern) && !allowNonProdRel)
{
    PrintError($"Current branch '{currentBranch}' is not a prod-rel branch.");
    PrintInfo("This script must be run from a deploy/prod-rel-*, deploy/test-prod-rel-*, or test/prod-rel-* branch.");
    PrintInfo("Create a prod-rel branch first: git checkout -b deploy/prod-rel-YYYYMMDD");
    PrintInfo("Or re-run with --allow-non-prod-rel to override this check.");
    return 1;
}

if (!Regex.IsMatch(currentBranch, prodRelPattern) && allowNonProdRel)
{
    PrintWarning($"Branch check bypassed for '{currentBranch}'.");
}

PrintSuccess($"Branch verified: {currentBranch}");

// --- Step 2: Fetch origin/deploy/prod ---
PrintStep("Step 2: Fetch origin/deploy/prod");
try
{
    RunGit("fetch origin deploy/prod");
    PrintSuccess("Fetched origin/deploy/prod");
}
catch (Exception ex)
{
    PrintError($"Failed to fetch origin/deploy/prod: {ex.Message}");
    return 1;
}

// --- Step 3: Detect changed packable projects ---
PrintStep("Step 3: Detect changed packable projects");

if (!Directory.Exists(srcDir))
{
    PrintError($"Source directory not found: {srcDir}");
    return 1;
}

// Find all .csproj files in src/
var allCsprojFiles = Directory.GetFiles(srcDir, "*.csproj", SearchOption.AllDirectories);
var packableProjects = new List<(string csprojPath, string packageName, string currentVersion)>();

foreach (var csprojPath in allCsprojFiles)
{
    var doc = XDocument.Load(csprojPath);
    var ns = GetMsBuildNamespace(doc);
    
    // Get current VersionPrefix first
    var versionPrefixElement = doc.Descendants(ns + "VersionPrefix").FirstOrDefault();
    
    // Check if IsPackable is explicitly set
    var isPackableElement = doc.Descendants(ns + "IsPackable").FirstOrDefault();
    
    // Project is packable if:
    // 1. IsPackable is explicitly "true", OR
    // 2. IsPackable is not set AND VersionPrefix exists (libraries meant to be packaged)
    bool isPackable = false;
    if (isPackableElement != null)
    {
        isPackable = string.Equals(isPackableElement.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }
    else if (versionPrefixElement != null)
    {
        // No explicit IsPackable, but has VersionPrefix = likely a packable library
        isPackable = true;
    }
    
    if (!isPackable)
    {
        continue;
    }
    
    // Get PackageId (or fallback to project name)
    var packageIdElement = doc.Descendants(ns + "PackageId").FirstOrDefault();
    var packageName = packageIdElement?.Value ?? Path.GetFileNameWithoutExtension(csprojPath);
    
    var currentVersion = versionPrefixElement?.Value ?? "1.0.0";
    
    packableProjects.Add((csprojPath, packageName, currentVersion));
}

PrintInfo($"Found {packableProjects.Count} packable projects");

// Detect which projects have changes
var changedProjects = new List<(string csprojPath, string packageName, string currentVersion)>();

foreach (var (csprojPath, packageName, currentVersion) in packableProjects)
{
    var projectDir = Path.GetDirectoryName(csprojPath);
    var relativeDir = Path.GetRelativePath(repoRoot, projectDir).Replace('\\', '/');
    
    try
    {
        var diffOutput = RunGit($"diff --name-only origin/deploy/prod HEAD -- {relativeDir}/");
        if (!string.IsNullOrWhiteSpace(diffOutput))
        {
            changedProjects.Add((csprojPath, packageName, currentVersion));
            Console.WriteLine($"  📝 {packageName} (changed)");
        }
    }
    catch (Exception ex)
    {
        PrintWarning($"Failed to check changes for {packageName}: {ex.Message}");
    }
}

if (changedProjects.Count == 0)
{
    PrintSuccess("No changed packable projects detected.");
}
else
{
    PrintInfo($"Detected {changedProjects.Count} changed projects that need version bumps");
}

// --- Step 4: Auto-bump MINOR versions ---
PrintStep("Step 4: Bump MINOR versions for changed projects");

var versionBumps = new List<(string packageName, string oldVersion, string newVersion, string csprojPath)>();

foreach (var (csprojPath, packageName, currentVersion) in changedProjects)
{
    var match = Regex.Match(currentVersion, @"^(\d+)\.(\d+)\.(\d+)");
    if (!match.Success)
    {
        PrintWarning($"Skipping {packageName}: Invalid version format '{currentVersion}'");
        continue;
    }

    // Always calculate target version from origin/deploy/prod (not current)
    var relativePath = Path.GetRelativePath(repoRoot, csprojPath).Replace('\\', '/');
    string prodVersion = null;
    string sitVersion = null;

    try
    {
        var prodContent = RunGit($"show origin/deploy/prod:{relativePath}");
        var prodDoc = XDocument.Parse(prodContent);
        var prodNs = GetMsBuildNamespace(prodDoc);
        var prodVersionElement = prodDoc.Descendants(prodNs + "VersionPrefix").FirstOrDefault();
        if (prodVersionElement == null)
        {
            prodVersionElement = prodDoc.Descendants(prodNs + "Version").FirstOrDefault();
        }
        prodVersion = prodVersionElement?.Value;
    }
    catch { }

    // Get SIT/Dev version from Directory.Packages.props if available
    try
    {
        var devDoc = XDocument.Load(packagesPropsPath);
        var devNs = GetMsBuildNamespace(devDoc);
        var devPkg = devDoc.Descendants(devNs + "PackageVersion").FirstOrDefault(p => (p.Attribute("Include")?.Value ?? "") == packageName);
        sitVersion = devPkg?.Attribute("Version")?.Value;
    }
    catch { }

    // Only bump if dev (sitVersion) > prod (prodVersion)
    Version vProd = null, vSit = null;
    if (Version.TryParse(prodVersion, out var v1)) vProd = v1;
    if (Version.TryParse(sitVersion, out var v2)) vSit = v2;

    if (vSit != null && (vProd == null || vSit > vProd))
    {
        var targetVersion = sitVersion;
        if (currentVersion == targetVersion)
        {
            PrintInfo($"  ✓ {packageName}: Already at target version {targetVersion}, skipping");
            continue;
        }
        versionBumps.Add((packageName, currentVersion, targetVersion, csprojPath));
        if (!dryRun)
        {
            // Update .csproj file
            var doc = XDocument.Load(csprojPath);
            var ns = GetMsBuildNamespace(doc);
            var versionPrefixElement = doc.Descendants(ns + "VersionPrefix").FirstOrDefault();
            if (versionPrefixElement != null)
            {
                versionPrefixElement.Value = targetVersion;
                doc.Save(csprojPath);
            }
            else
            {
                PrintWarning($"No VersionPrefix found in {csprojPath}, skipping");
                continue;
            }
        }
        Console.WriteLine($"  📦 {packageName,-50} {currentVersion,8} → {targetVersion,8}");
    }
    else
    {
        PrintInfo($"  ✓ {packageName}: Prod version {prodVersion} is up-to-date or ahead, no bump needed.");
        continue;
    }
}

if (dryRun)
{
    PrintInfo("Dry-run: .csproj files not modified");
}
else
{
    PrintSuccess($"Updated {versionBumps.Count} .csproj files");
}

// --- Step 5: Update Directory.Packages.Prod.props ---
PrintStep("Step 5: Sync Directory.Packages.Prod.props with Dev props");

if (!File.Exists(prodPropsPath))
{
    PrintError($"Directory.Packages.Prod.props not found: {prodPropsPath}");
    return 1;
}

// Load both props files
var devDoc = XDocument.Load(packagesPropsPath);
var devNs = GetMsBuildNamespace(devDoc);
var devPackages = devDoc.Descendants(devNs + "PackageVersion").ToList();

var prodPropsDoc = XDocument.Load(prodPropsPath);
var prodNs = GetMsBuildNamespace(prodPropsDoc);
var prodItemGroup = prodPropsDoc.Descendants(prodNs + "ItemGroup").FirstOrDefault();
if (prodItemGroup == null)
{
    PrintError("No ItemGroup found in Directory.Packages.Prod.props");
    return 1;
}

var prodPackageVersions = prodPropsDoc.Descendants(prodNs + "PackageVersion").ToList();

// Build dev package dictionary
var devPackageDict = devPackages.ToDictionary(
    p => p.Attribute("Include")?.Value ?? "",
    p => p.Attribute("Version")?.Value ?? ""
);

// Build prod package dictionary
var prodPackageDict = prodPackageVersions.ToDictionary(
    p => p.Attribute("Include")?.Value ?? "",
    p => p
);

var prodPropsUpdates = new List<(string action, string packageName, string oldVersion, string newVersion)>();

// 1. Update bumped local packages
var localPackageVersions = versionBumps.ToDictionary(v => v.packageName, v => v.newVersion);
foreach (var packageElement in prodPackageVersions)
{
    var packageName = packageElement.Attribute("Include")?.Value;
    var currentVersion = packageElement.Attribute("Version")?.Value;
    
    if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(currentVersion))
        continue;
    
    if (localPackageVersions.TryGetValue(packageName, out var newVersion))
    {
        prodPropsUpdates.Add(("Local bump", packageName, currentVersion, newVersion));
        if (!dryRun)
            packageElement.SetAttributeValue("Version", newVersion);
    }
}

// 2. Fix pre-release suffixes (remove -dev/-sit and bump MINOR to indicate base version)
foreach (var packageElement in prodPackageVersions)
{
    var packageName = packageElement.Attribute("Include")?.Value;
    var currentVersion = packageElement.Attribute("Version")?.Value;
    
    if (packageName?.StartsWith("Share.") == true && currentVersion != null)
    {
        if (currentVersion.Contains("-dev") || currentVersion.Contains("-sit"))
        {
            var baseVersion = Regex.Replace(currentVersion, @"-(dev|sit).*$", "");
            
            // Also bump MINOR and reset PATCH to 0 for consistency
            var versionMatch = Regex.Match(baseVersion, @"^(\d+)\.(\d+)");
            if (versionMatch.Success)
            {
                var major = versionMatch.Groups[1].Value;
                var minor = (int.Parse(versionMatch.Groups[2].Value) + 1).ToString();
                baseVersion = $"{major}.{minor}.0";
            }
            
            prodPropsUpdates.Add(("Remove suffix", packageName, currentVersion, baseVersion));
            if (!dryRun)
                packageElement.SetAttributeValue("Version", baseVersion);
        }
    }
}

// 3. Fix MINOR mismatches (sync from Dev, bumping MINOR and resetting PATCH to 0)
foreach (var devPkg in devPackages)
{
    var packageName = devPkg.Attribute("Include")?.Value;
    var devVersion = devPkg.Attribute("Version")?.Value;
    
    if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(devVersion) || !packageName.StartsWith("Share."))
        continue;
    
    if (prodPackageDict.TryGetValue(packageName, out var prodElement))
    {
        var prodVersion = prodElement.Attribute("Version")?.Value ?? "";
        var devBaseVersion = Regex.Replace(devVersion, @"-(dev|sit).*$", "");
        var prodBaseVersion = Regex.Replace(prodVersion, @"-(dev|sit).*$", "");
        var devMatch = Regex.Match(devBaseVersion, @"^(\d+)\.(\d+)\.(\d+)");
        var prodMatch = Regex.Match(prodBaseVersion, @"^(\d+)\.(\d+)\.(\d+)");
        if (devMatch.Success && prodMatch.Success)
        {
            var devMajor = int.Parse(devMatch.Groups[1].Value);
            var devMinor = int.Parse(devMatch.Groups[2].Value);
            var devPatch = int.Parse(devMatch.Groups[3].Value);
            var prodMajor = int.Parse(prodMatch.Groups[1].Value);
            var prodMinor = int.Parse(prodMatch.Groups[2].Value);
            var prodPatch = int.Parse(prodMatch.Groups[3].Value);
            string targetVersion;
            if (devMinor > prodMinor)
            {
                if (devPatch == 0)
                {
                    targetVersion = $"{devMajor}.{devMinor}.0";
                }
                else
                {
                    targetVersion = $"{devMajor}.{devMinor + 1}.0";
                }
            }
            else if (devMinor == prodMinor)
            {
                targetVersion = $"{devMajor}.{devMinor}.0";
            }
            else
            {
                // Dev is behind prod (e.g., hotfix or manual bump) - keep prod as-is
                targetVersion = prodBaseVersion;
            }
            if (targetVersion != prodBaseVersion)
            {
                prodPropsUpdates.Add(("Version sync", packageName, prodVersion, targetVersion));
                if (!dryRun)
                    prodElement.SetAttributeValue("Version", targetVersion);
            }
        }
    }
}

// 4. Add missing Share.* packages from Dev to Prod
var prodSharePackages = prodPackageVersions
    .Select(p => p.Attribute("Include")?.Value)
    .Where(name => !string.IsNullOrEmpty(name) && name.StartsWith("Share."))
    .ToHashSet();

foreach (var devPkg in devPackages)
{
    var packageName = devPkg.Attribute("Include")?.Value;
    var devVersion = devPkg.Attribute("Version")?.Value;
    
    if (string.IsNullOrEmpty(packageName) || !packageName.StartsWith("Share."))
        continue;
    
    if (!prodSharePackages.Contains(packageName))
    {
        var devBaseVersion = Regex.Replace(devVersion, @"-(dev|sit).*$", "");
        
        // Extract MAJOR.MINOR, bump MINOR, set PATCH to 0
        var versionMatch = Regex.Match(devBaseVersion, @"^(\d+)\.(\d+)");
        var baseVersion = devBaseVersion; // Default if no match
        if (versionMatch.Success)
        {
            var major = versionMatch.Groups[1].Value;
            var minor = (int.Parse(versionMatch.Groups[2].Value) + 1).ToString();
            baseVersion = $"{major}.{minor}.0";
        }
        
        prodPropsUpdates.Add(("Add missing", packageName, "", baseVersion));
        
        if (!dryRun)
        {
            var newElement = new XElement(prodNs + "PackageVersion",
                new XAttribute("Include", packageName),
                new XAttribute("Version", baseVersion));
            prodItemGroup.Add(newElement);
        }
    }
}

// 5. Reset PATCH to 0 and bump MINOR for all Share.* packages (base version enforcement)
foreach (var packageElement in prodPackageVersions)
{
    var packageName = packageElement.Attribute("Include")?.Value;
    var currentVersion = packageElement.Attribute("Version")?.Value;
    
    if (packageName?.StartsWith("Share.") == true && !string.IsNullOrEmpty(currentVersion))
    {
        var versionMatch = Regex.Match(currentVersion, @"^(\d+)\.(\d+)\.(\d+)");
        if (versionMatch.Success)
        {
            var major = versionMatch.Groups[1].Value;
            var minor = int.Parse(versionMatch.Groups[2].Value);
            var patch = int.Parse(versionMatch.Groups[3].Value);
            
            if (patch != 0)
            {
                // Bump MINOR and set PATCH to 0 for base version format
                var minorBumped = (minor + 1).ToString();
                var baseVersion = $"{major}.{minorBumped}.0";
                
                // Check if we already added this as a fix
                var existingFix = prodPropsUpdates.FirstOrDefault(u => u.packageName == packageName && u.newVersion == baseVersion);
                if (existingFix == default)
                {
                    prodPropsUpdates.Add(("Base version", packageName, currentVersion, baseVersion));
                }
                
                if (!dryRun)
                    packageElement.SetAttributeValue("Version", baseVersion);
            }
        }
    }
}

// Display updates
if (prodPropsUpdates.Count == 0)
{
    PrintSuccess("Directory.Packages.Prod.props is already in sync");
}
else
{
    Console.WriteLine($"  📋 Applying {prodPropsUpdates.Count} fix(es):");
    foreach (var (action, pkg, oldVer, newVer) in prodPropsUpdates)
    {
        var oldDisplay = string.IsNullOrEmpty(oldVer) ? "(new)" : oldVer;
        Console.WriteLine($"     • {action,-15} {pkg,-45} {oldDisplay,10} → {newVer}");
    }
    
    if (dryRun)
    {
        PrintInfo($"Dry-run: Directory.Packages.Prod.props not modified");
    }
    else
    {
        prodPropsDoc.Save(prodPropsPath);
        PrintSuccess($"Updated Directory.Packages.Prod.props");
    }
}

// --- Step 6: Run validation checks ---
PrintStep("Step 6: Run validation checks");

var validationErrors = new List<string>();

// Reload files to validate final state after auto-fixes
var prodDocValidate = XDocument.Load(prodPropsPath);
var prodNsValidate = GetMsBuildNamespace(prodDocValidate);
var prodPackagesValidate = prodDocValidate.Descendants(prodNsValidate + "PackageVersion");

// Check 1: No pre-release suffixes in Prod.props
foreach (var pkg in prodPackagesValidate)
{
    var include = pkg.Attribute("Include")?.Value;
    var version = pkg.Attribute("Version")?.Value;
    
    if (include?.StartsWith("Share.") == true && version != null)
    {
        if (version.Contains("-dev") || version.Contains("-sit"))
        {
            validationErrors.Add($"Pre-release version in Prod.props: {include} = {version}");
        }
    }
}

// Check 2: Version alignment (Dev X.Y.N → Prod X.(Y+1).0)
var devDocValidate = XDocument.Load(packagesPropsPath);
var devNsValidate = GetMsBuildNamespace(devDocValidate);
var devPackagesValidate = devDocValidate.Descendants(devNsValidate + "PackageVersion").ToList();

var prodPackageDictValidate = prodPackagesValidate.ToDictionary(
    p => p.Attribute("Include")?.Value ?? "",
    p => p.Attribute("Version")?.Value ?? ""
);

foreach (var devPkg in devPackagesValidate)
{
    var packageName = devPkg.Attribute("Include")?.Value;
    var devVersion = devPkg.Attribute("Version")?.Value;
    
    if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(devVersion))
    {
        continue;
    }
    
    if (!packageName.StartsWith("Share."))
    {
        continue;
    }
    
    if (prodPackageDictValidate.TryGetValue(packageName, out var prodVersion))
    {
        var devBaseVersion = Regex.Replace(devVersion, @"-(dev|sit).*$", "");
        var prodBaseVersion = Regex.Replace(prodVersion, @"-(dev|sit).*$", "");
        
        var devMatch = Regex.Match(devBaseVersion, @"^(\d+)\.(\d+)\.(\d+)");
        var prodMatch = Regex.Match(prodBaseVersion, @"^(\d+)\.(\d+)\.(\d+)");
        
        if (devMatch.Success && prodMatch.Success)
        {
            var devMajor = int.Parse(devMatch.Groups[1].Value);
            var devMinor = int.Parse(devMatch.Groups[2].Value);
            var devPatch = int.Parse(devMatch.Groups[3].Value);
            var prodMajor = int.Parse(prodMatch.Groups[1].Value);
            var prodMinor = int.Parse(prodMatch.Groups[2].Value);
            var prodPatch = int.Parse(prodMatch.Groups[3].Value);
            
            string expectedVersion;
            if (devMinor > prodMinor)
            {
                if (devPatch == 0)
                {
                    expectedVersion = $"{devMajor}.{devMinor}.0";
                }
                else
                {
                    expectedVersion = $"{devMajor}.{devMinor + 1}.0";
                }
            }
            else if (devMinor == prodMinor)
            {
                expectedVersion = $"{devMajor}.{devMinor}.0";
            }
            else
            {
                // Dev is behind prod (e.g., hotfix or manual bump) - keep prod as-is
                expectedVersion = prodBaseVersion;
            }
            
            Version prodV, expectedV;
            if (Version.TryParse(prodBaseVersion, out prodV) && Version.TryParse(expectedVersion, out expectedV))
            {
                if (prodV < expectedV)
                {
                    validationErrors.Add($"Version alignment: {packageName} → Dev={devBaseVersion}, Prod={prodVersion}, Expected={expectedVersion}");
                }
            }
        }
    }
}

// Check 3: Package parity
var devSharePackagesValidate = devPackagesValidate
    .Select(p => p.Attribute("Include")?.Value)
    .Where(name => !string.IsNullOrEmpty(name) && name.StartsWith("Share."))
    .ToHashSet();

var prodSharePackagesValidate = prodPackagesValidate
    .Select(p => p.Attribute("Include")?.Value)
    .Where(name => !string.IsNullOrEmpty(name) && name.StartsWith("Share."))
    .ToHashSet();

var missingInProdValidate = devSharePackagesValidate.Except(prodSharePackagesValidate).ToList();
var missingInDevValidate = prodSharePackagesValidate.Except(devSharePackagesValidate).ToList();

if (missingInProdValidate.Any())
{
    validationErrors.Add($"Packages in Directory.Packages.props but missing in Prod.props: {string.Join(", ", missingInProdValidate)}");
}

if (missingInDevValidate.Any())
{
    validationErrors.Add($"Packages in Prod.props but missing in Directory.Packages.props: {string.Join(", ", missingInDevValidate)}");
}

// Check 4: NuGet feed availability - verify versions are published on gitlab
Console.WriteLine("🔍 Check 4: Verifying Prod.props versions on gitlab feed...");
var unavailableOnFeed = new List<(string package, string version)>();

foreach (var prodPkg in prodPackagesValidate)
{
    var packageName = prodPkg.Attribute("Include")?.Value;
    var version = prodPkg.Attribute("Version")?.Value;
    
    if (string.IsNullOrEmpty(packageName) || !packageName.StartsWith("Share."))
        continue;
    
    try
    {
        // Use dotnet package search to query gitlab feed only
        var searchProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"package search \"{packageName}\" --exact-match --prerelease --source gitlab --format json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            }
        };

        searchProcess.Start();
        var output = searchProcess.StandardOutput.ReadToEnd();
        searchProcess.WaitForExit();

        if (string.IsNullOrWhiteSpace(output))
        {
            unavailableOnFeed.Add((packageName, version));
            continue;
        }

        // Check if the specific version appears in the JSON output
        var versionFound = output.Contains($"\"version\": \"{version}\"");
        if (!versionFound)
        {
            versionFound = Regex.IsMatch(output, $"\"version\"\\s*:\\s*\"{Regex.Escape(version)}\"", RegexOptions.IgnoreCase);
        }

        if (!versionFound)
        {
            unavailableOnFeed.Add((packageName, version));
        }
    }
    catch
    {
        // Silently continue - best effort check
    }
}

if (unavailableOnFeed.Any())
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"❌ CRITICAL: {unavailableOnFeed.Count} version(s) NOT on gitlab:");
    Console.ResetColor();
    foreach (var (pkg, ver) in unavailableOnFeed.OrderBy(x => x.package))
    {
        Console.WriteLine($"   • {pkg} v{ver}");
    }
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("These packages MUST be published to gitlab before prod merge!");
    Console.WriteLine();
    Console.WriteLine("To publish:");
    Console.WriteLine("   1. cd to the repository with the package");
    Console.WriteLine("   2. dotnet pack -c Release");
    Console.WriteLine("   3. dotnet nuget push bin/Release/*.nupkg -s <gitlab-feed-url>");
    Console.WriteLine("   4. Wait ~5 mins for gitlab indexing");
    Console.WriteLine("   5. Verify: dotnet package search {packageName} --exact-match --prerelease --source gitlab --format json");
    Console.ResetColor();
    Console.WriteLine();
    validationErrors.Add($"Check 4 FAILED: {unavailableOnFeed.Count} versions not on gitlab");
}

// Display validation results
if (validationErrors.Any())
{
    PrintError("Validation failed - manual fixes needed:");
    Console.WriteLine();
    foreach (var error in validationErrors)
    {
        Console.WriteLine($"  • {error}");
    }
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("📝 Next steps:");
    Console.ResetColor();
    Console.WriteLine("   1. Fix the issues above in Directory.Packages.props and Directory.Packages.Prod.props");
    Console.WriteLine("   2. Re-run: dotnet script .husky/csx/prep-prod-rel.csx");
    Console.WriteLine("   3. Commit the changes");
    Console.WriteLine();
    return 1;
}
else
{
    PrintSuccess("All validation checks passed");
}

// --- Step 7: Print summary ---
Console.WriteLine();
PrintStep("Summary");

if (versionBumps.Any())
{
    Console.WriteLine();
    Console.WriteLine("📦 \u001b[1mVersion bumps:\u001b[0m");
    foreach (var (pkgName, oldVer, newVer, _) in versionBumps)
    {
        Console.WriteLine($"   {pkgName,-50} {oldVer,8} → {newVer,8}");
    }
}

if (prodPropsUpdates.Any())
{
    Console.WriteLine();
    Console.WriteLine("📋 \u001b[1mDirectory.Packages.Prod.props updates:\u001b[0m");
    foreach (var (action, pkgName, oldVer, newVer) in prodPropsUpdates)
    {
        var oldDisplay = string.IsNullOrEmpty(oldVer) ? "(new)" : oldVer;
        Console.WriteLine($"   [{action}] {pkgName,-45} {oldDisplay,10} → {newVer}");
    }
}

Console.WriteLine();
PrintSuccess("All validation checks passed");

if (dryRun)
{
    Console.WriteLine();
    PrintInfo("Dry-run completed. No files were modified.");
    PrintInfo("Run without --dry-run to apply changes.");
    return 0;
}

// --- Step 8: Confirmation and staging ---
Console.WriteLine();
Console.Write("Apply these changes and stage files for commit? [y/N]: ");
var response = Console.ReadLine()?.Trim().ToLower();

if (response != "y" && response != "yes")
{
    PrintWarning("Changes not staged. Files have been modified but not staged.");
    PrintInfo("You can manually review and stage with: git add Directory.Packages.Prod.props src/*/");
    return 0;
}

// Stage modified files
var filesToStage = new List<string>();
filesToStage.Add("Directory.Packages.Prod.props");
filesToStage.AddRange(versionBumps.Select(v => Path.GetRelativePath(repoRoot, v.csprojPath).Replace('\\', '/')));

foreach (var file in filesToStage)
{
    try
    {
        RunGit($"add {file}");
        Console.WriteLine($"  📝 Staged: {file}");
    }
    catch (Exception ex)
    {
        PrintWarning($"Failed to stage {file}: {ex.Message}");
    }
}

Console.WriteLine();
PrintSuccess("Changes applied and staged successfully!");
PrintInfo("Next steps:");
PrintInfo("  1. Review staged changes: git diff --staged");
PrintInfo("  2. Commit: git commit -m \"chore: HSAMED-#### bump versions for prod release\"");
PrintInfo("  3. Run validation: dotnet script scripts/prod-rel-validate.csx");

return 0;
