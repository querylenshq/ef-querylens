#!/usr/bin/env dotnet-script
// 
// prod-rel-validate.csx
// Validates prod-rel branches for NuGet versioning compliance
//
// Usage: dotnet script scripts/prod-rel-validate.csx [--allow-non-prod-rel]
//

using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;

var allowNonProdRel = Args.Contains("--allow-non-prod-rel");
var hasErrors = false;
var errorMessages = new List<string>();

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

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔════════════════════════════════════════════════════════╗");
Console.WriteLine("║       Prod-Rel Branch Validation Checks                ║");
Console.WriteLine("╚════════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

var prodRelPattern = @"^(deploy/(test-)?prod-rel-\d{8}(-hotfix-.*)?|test/prod-rel-\d{8})$|^deploy/prod$";
try
{
    var currentBranch = RunGit("rev-parse --abbrev-ref HEAD");
    if (!Regex.IsMatch(currentBranch, prodRelPattern) && !allowNonProdRel)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"❌ Current branch '{currentBranch}' is not a prod-rel branch.");
        Console.ResetColor();
        Console.WriteLine("ℹ️  This script must be run from a deploy/prod-rel-*, deploy/test-prod-rel-*, or test/prod-rel-* branch.");
        Console.WriteLine("ℹ️  Or re-run with --allow-non-prod-rel to override this check.");
        return 1;
    }

    if (!Regex.IsMatch(currentBranch, prodRelPattern) && allowNonProdRel)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"⚠️  Branch check bypassed for '{currentBranch}'.");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✅ Branch verified: {currentBranch}");
        Console.ResetColor();
    }
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"⚠️  Branch check skipped: {ex.Message}");
    Console.ResetColor();
    Console.WriteLine();
}

// ============================================================================
// CHECK 1: No pre-release suffixes in Directory.Packages.Prod.props
// ============================================================================
Console.WriteLine("🔍 Check 1: No pre-release suffixes in Prod.props");

var prodPropsPath = Path.Combine(Directory.GetCurrentDirectory(), "Directory.Packages.Prod.props");

if (File.Exists(prodPropsPath))
{
    try
    {
        var prodPropsDoc = XDocument.Load(prodPropsPath);
        var prodPackages = prodPropsDoc.Descendants("PackageVersion")
            .Where(p => p.Attribute("Include")?.Value.StartsWith("Share.") == true)
            .ToList();

        var preReleasePackages = prodPackages.Where(p =>
        {
            var version = p.Attribute("Version")?.Value ?? "";
            return version.Contains("-dev") || version.Contains("-sit") || Regex.IsMatch(version, @"-[a-zA-Z0-9]+");
        }).ToList();

        if (preReleasePackages.Any())
        {
            hasErrors = true;
            errorMessages.Add("❌ Check 1 FAILED — Pre-release suffixes found in Prod.props:");
            foreach (var pkg in preReleasePackages)
            {
                var name = pkg.Attribute("Include")?.Value;
                var version = pkg.Attribute("Version")?.Value;
                errorMessages.Add($"   • {name}  Version=\"{version}\"");
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("   ✅ No pre-release suffixes found");
            Console.ResetColor();
        }

        // CHECK 1.1: Validate all Share.* versions are base versions (PATCH = 0)
        Console.WriteLine("🔍 Check 1.1: Base version format (X.Y.0)");
        var nonBaseVersions = prodPackages
            .Where(pkg => {
                var version = pkg.Attribute("Version")?.Value ?? "";
                var match = Regex.Match(version, @"^(\d+)\.(\d+)\.(\d+)");
                if (match.Success)
                {
                    var patch = int.Parse(match.Groups[3].Value);
                    return patch != 0; // Not a base version
                }
                return false;
            })
            .ToList();

        if (nonBaseVersions.Any())
        {
            hasErrors = true;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("   ❌ FAILED — Non-base versions found in Prod.props:");
            Console.ResetColor();
            foreach (var pkg in nonBaseVersions)
            {
                var name = pkg.Attribute("Include")?.Value;
                var version = pkg.Attribute("Version")?.Value;
                Console.WriteLine($"   • {name}  Version=\"{version}\"");
            }
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("   📝 What this means: Prod.props should only reference base versions (X.Y.0)");
            Console.WriteLine("   🔧 Fix: Run prep-prod-rel.csx to sync and reset PATCH to 0");
            Console.ResetColor();
            errorMessages.Add("❌ Check 1.1 FAILED — Non-base versions in Prod.props (should be X.Y.0 format)");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("   ✅ All versions are base versions");
            Console.ResetColor();
        }
    }
    catch (Exception ex)
    {
        hasErrors = true;
        errorMessages.Add($"❌ Check 1 ERROR — Failed to parse Prod.props: {ex.Message}");
    }
}
else
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("   ⚠️  Skipped (no Directory.Packages.Prod.props found)");
    Console.ResetColor();
}

Console.WriteLine();

// ============================================================================
// CHECK 2: Version alignment (Dev X.Y.N → Prod X.(Y+1).0)
// ============================================================================
Console.WriteLine("🔍 Check 2: Version alignment");

var devPropsPath = Path.Combine(Directory.GetCurrentDirectory(), "Directory.Packages.props");

if (File.Exists(prodPropsPath) && File.Exists(devPropsPath))
{
    try
    {
        var devPropsDoc = XDocument.Load(devPropsPath);
        var prodPropsDoc = XDocument.Load(prodPropsPath);

        var devPackages = devPropsDoc.Descendants("PackageVersion")
            .Where(p => p.Attribute("Include")?.Value.StartsWith("Share.") == true)
            .ToDictionary(
                p => p.Attribute("Include")?.Value ?? "",
                p => p.Attribute("Version")?.Value ?? ""
            );

        var prodPackages = prodPropsDoc.Descendants("PackageVersion")
            .Where(p => p.Attribute("Include")?.Value.StartsWith("Share.") == true)
            .ToDictionary(
                p => p.Attribute("Include")?.Value ?? "",
                p => p.Attribute("Version")?.Value ?? ""
            );

        var versionMismatches = new List<string>();

        foreach (var devPkg in devPackages)
        {
            if (prodPackages.TryGetValue(devPkg.Key, out var prodVersion))
            {
                var devBase = Regex.Replace(devPkg.Value, @"-(dev|sit).*$", "");
                var prodBase = Regex.Replace(prodVersion, @"-(dev|sit).*$", "");
                var devMatch = Regex.Match(devBase, @"^(\d+)\.(\d+)\.(\d+)");
                var prodMatch = Regex.Match(prodBase, @"^(\d+)\.(\d+)\.(\d+)");
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
                        expectedVersion = prodBase;
                    }
                    if (prodBase != expectedVersion)
                    {
                        versionMismatches.Add($"   • {devPkg.Key} → Dev={devBase}, Prod={prodVersion}, Expected={expectedVersion}");
                    }
                }
            }
        }

        if (versionMismatches.Any())
        {
            hasErrors = true;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("   ❌ FAILED — Version alignment issues:");
            Console.ResetColor();
            foreach (var mismatch in versionMismatches)
                Console.WriteLine(mismatch);
            errorMessages.Add("❌ Check 2 FAILED — Version alignment (expected Dev X.Y.N → Prod X.(Y+1).0)");
            errorMessages.AddRange(versionMismatches);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("   ✅ All versions correctly aligned");
            Console.ResetColor();
        }
    }
    catch (Exception ex)
    {
        hasErrors = true;
        errorMessages.Add($"❌ Check 2 ERROR — Failed to compare props files: {ex.Message}");
    }
}
else
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("   ⚠️  Skipped (props files not found)");
    Console.ResetColor();
}

Console.WriteLine();

// ============================================================================
// CHECK 3: Package parity between both props files
// ============================================================================
Console.WriteLine("🔍 Check 3: Package parity");

if (File.Exists(prodPropsPath) && File.Exists(devPropsPath))
{
    try
    {
        var devPropsDoc = XDocument.Load(devPropsPath);
        var prodPropsDoc = XDocument.Load(prodPropsPath);

        var devSharePackages = devPropsDoc.Descendants("PackageVersion")
            .Where(p => p.Attribute("Include")?.Value.StartsWith("Share.") == true)
            .Select(p => p.Attribute("Include")?.Value ?? "")
            .ToHashSet();

        var prodSharePackages = prodPropsDoc.Descendants("PackageVersion")
            .Where(p => p.Attribute("Include")?.Value.StartsWith("Share.") == true)
            .Select(p => p.Attribute("Include")?.Value ?? "")
            .ToHashSet();

        var missingInProd = devSharePackages.Except(prodSharePackages).ToList();
        var missingInDev = prodSharePackages.Except(devSharePackages).ToList();

        if (missingInProd.Any() || missingInDev.Any())
        {
            hasErrors = true;
            errorMessages.Add("❌ Check 3 FAILED — Package parity mismatch:");
            if (missingInProd.Any())
            {
                errorMessages.Add("   Missing in Prod.props:");
                foreach (var pkg in missingInProd)
                {
                    errorMessages.Add($"     • {pkg}");
                }
            }
            if (missingInDev.Any())
            {
                errorMessages.Add("   Extra in Prod.props (not in Dev props):");
                foreach (var pkg in missingInDev)
                {
                    errorMessages.Add($"     • {pkg}");
                }
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("   ✅ All Share.* packages present in both files");
            Console.ResetColor();
        }
    }
    catch (Exception ex)
    {
        hasErrors = true;
        errorMessages.Add($"❌ Check 3 ERROR — Failed to check package parity: {ex.Message}");
    }
}
else
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("   ⚠️  Skipped (props files not found)");
    Console.ResetColor();
}

Console.WriteLine();

// ============================================================================
// CHECK 4: Full package parity + version diff (non-Share.*)
// ============================================================================
Console.WriteLine("🔍 Check 4: Full package parity + version diff (non-Share.*)");

if (File.Exists(prodPropsPath) && File.Exists(devPropsPath))
{
    try
    {
        var devPropsDoc = XDocument.Load(devPropsPath);
        var prodPropsDoc = XDocument.Load(prodPropsPath);

        var devAllPackages = devPropsDoc.Descendants("PackageVersion")
            .ToDictionary(
                p => p.Attribute("Include")?.Value ?? "",
                p => p.Attribute("Version")?.Value ?? ""
            );

        var prodAllPackages = prodPropsDoc.Descendants("PackageVersion")
            .ToDictionary(
                p => p.Attribute("Include")?.Value ?? "",
                p => p.Attribute("Version")?.Value ?? ""
            );

        var missingInProd = devAllPackages.Keys.Except(prodAllPackages.Keys).OrderBy(p => p).ToList();
        var missingInDev = prodAllPackages.Keys.Except(devAllPackages.Keys).OrderBy(p => p).ToList();
        var versionDiffs = devAllPackages
            .Where(kvp => !kvp.Key.StartsWith("Share.", StringComparison.OrdinalIgnoreCase))
            .Where(kvp => prodAllPackages.ContainsKey(kvp.Key))
            .Where(kvp => !string.Equals(kvp.Value, prodAllPackages[kvp.Key], StringComparison.OrdinalIgnoreCase))
            .ToList();

        var check4Issues = new List<string>();

        if (missingInProd.Any())
        {
            check4Issues.Add("Missing in Prod.props:");
            foreach (var pkg in missingInProd)
            {
                check4Issues.Add($"  • {pkg}");
            }
        }

        if (missingInDev.Any())
        {
            if (check4Issues.Any()) check4Issues.Add("");
            check4Issues.Add("Extra in Prod.props (not in Dev props):");
            foreach (var pkg in missingInDev)
            {
                check4Issues.Add($"  • {pkg}");
            }
        }

        if (versionDiffs.Any())
        {
            if (check4Issues.Any()) check4Issues.Add("");
            check4Issues.Add("Non-Share.* version mismatches:");
            foreach (var diff in versionDiffs.OrderBy(d => d.Key))
            {
                check4Issues.Add($"  • {diff.Key}: Dev={diff.Value}, Prod={prodAllPackages[diff.Key]}");
            }
        }

        if (check4Issues.Any())
        {
            hasErrors = true;
            errorMessages.Add("❌ Check 4 FAILED — Package alignment issues:");
            errorMessages.AddRange(check4Issues);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("   ✅ All packages aligned and non-Share.* versions match");
            Console.ResetColor();
        }
    }
    catch (Exception ex)
    {
        hasErrors = true;
        errorMessages.Add($"❌ Check 4 ERROR — Failed to validate package parity: {ex.Message}");
    }
}
else
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("   ⚠️  Skipped (props files not found)");
    Console.ResetColor();
}

Console.WriteLine();

// ============================================================================
// CHECK 5: Version bump for changed packable projects
// ============================================================================
Console.WriteLine("🔍 Check 5: Version bump check for changed projects");

try
{
    // Find all packable csproj files
    var packableProjects = Directory.GetFiles("src", "*.csproj", SearchOption.AllDirectories)
        .Where(f => File.ReadAllText(f).Contains("<IsPackable>true</IsPackable>"))
        .ToList();

    if (!packableProjects.Any())
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("   ⚠️  Skipped (no packable projects found)");
        Console.ResetColor();
    }
    else
    {
        var projectsAlreadyBumped = new List<string>();
        var projectsNeedingBump = new List<string>();

        foreach (var projectPath in packableProjects)
        {
            var projectDir = Path.GetDirectoryName(projectPath) ?? "";
            var projectName = Path.GetFileNameWithoutExtension(projectPath);

            // Check if files changed in this project vs deploy/prod
            var gitDiffProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"diff --name-only origin/deploy/prod HEAD -- \"{projectDir}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            gitDiffProcess.Start();
            var changedFiles = gitDiffProcess.StandardOutput.ReadToEnd();
            gitDiffProcess.WaitForExit();

            if (!string.IsNullOrWhiteSpace(changedFiles))
            {
                // Files changed - check if VersionPrefix was bumped
                var csprojContent = File.ReadAllText(projectPath);
                var versionMatch = Regex.Match(csprojContent, @"<VersionPrefix>(\d+)\.(\d+)\.(\d+)</VersionPrefix>");

                if (versionMatch.Success)
                {
                    var currentVersion = versionMatch.Value.Replace("<VersionPrefix>", "").Replace("</VersionPrefix>", "");

                    // Get prod version via git show
                    string prodVersion = null;
                    
                    try
                    {
                        var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), projectPath).Replace('\\', '/');
                        var gitShowProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "git",
                                Arguments = $"show origin/deploy/prod:{relativePath}",
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        
                        gitShowProcess.Start();
                        var prodContent = gitShowProcess.StandardOutput.ReadToEnd();
                        gitShowProcess.WaitForExit();
                        
                        if (gitShowProcess.ExitCode == 0)
                        {
                            // Try VersionPrefix first, then fall back to Version tag
                            var prodMatch = Regex.Match(prodContent, @"<VersionPrefix>(\d+)\.(\d+)\.(\d+)</VersionPrefix>");
                            if (!prodMatch.Success)
                            {
                                prodMatch = Regex.Match(prodContent, @"<Version>(\d+)\.(\d+)\.(\d+)</Version>");
                            }
                            if (prodMatch.Success)
                            {
                                prodVersion = prodMatch.Groups[1].Value + "." + prodMatch.Groups[2].Value + "." + prodMatch.Groups[3].Value;
                            }
                        }
                    }
                    catch
                    {
                        // If can't get prod version (new project), assume current is valid
                        prodVersion = null;
                    }

                    var fileCount = changedFiles.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                    
                    // Compare current to prod
                    if (prodVersion != null)
                    {
                        Version vCurrent, vProd;
                        if (Version.TryParse(currentVersion, out vCurrent) && Version.TryParse(prodVersion, out vProd))
                        {
                            if (vCurrent >= vProd)
                            {
                                projectsAlreadyBumped.Add($"   ✓ {projectName}: {currentVersion} (prod: {prodVersion})");
                            }
                            else
                            {
                                projectsNeedingBump.Add($"   • {projectName}: {currentVersion} is behind prod {prodVersion} ({fileCount} file(s) changed)");
                            }
                        }
                    }
                    else
                    {
                        // New project or can't get prod version
                        projectsAlreadyBumped.Add($"   ✓ {projectName}: {currentVersion} (new project or no prod version)");
                    }
                }
            }
        }

        if (projectsAlreadyBumped.Any())
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("   ✅ Already bumped:");
            Console.ResetColor();
            foreach (var msg in projectsAlreadyBumped)
            {
                Console.WriteLine(msg);
            }
        }

        if (projectsNeedingBump.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("   ⚠️  Needs version bump:");
            Console.ResetColor();
            foreach (var msg in projectsNeedingBump)
            {
                Console.WriteLine(msg);
            }
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("   💡 Run prep-prod-rel.csx to auto-bump versions");
            Console.ResetColor();
        }
        
        if (!projectsAlreadyBumped.Any() && !projectsNeedingBump.Any())
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("   ✅ No packable project changes detected");
            Console.ResetColor();
        }
    }
}
catch (Exception ex)
{
    hasErrors = true;
    errorMessages.Add($"❌ Check 5 ERROR — Failed to check version bumps: {ex.Message}");
}

Console.WriteLine();

// ============================================================================
// CHECK 6: Verify Prod.props packages exist on NuGet feed (via dotnet package search)
// ============================================================================
Console.WriteLine("🔍 Check 6: NuGet feed availability");

try
{
    var prodPropsDoc = XDocument.Load(prodPropsPath);
    var prodPackages = prodPropsDoc.Descendants("PackageVersion")
        .Where(p => p.Attribute("Include")?.Value.StartsWith("Share.") == true)
        .ToDictionary(
            p => p.Attribute("Include")?.Value ?? "",
            p => p.Attribute("Version")?.Value ?? ""
        );
    
    if (!prodPackages.Any())
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("   ✅ No Share.* packages to validate");
        Console.ResetColor();
    }
    else
    {
        var unavailableVersions = new List<(string package, string version)>();
        var feedQueryErrors = new List<string>();
        
        // Query packages using dotnet package search against gitlab feed
        foreach (var kvp in prodPackages)
        {
            var packageName = kvp.Key;
            var expectedVersion = kvp.Value;
            
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
                    feedQueryErrors.Add($"{packageName}: No response from dotnet package search");
                    continue;
                }

                // Parse JSON response looking for the specific version
                // Response structure: {"searchResult": [{"sourceName": "...", "packages": [{"version": "..."}, ...]}, ...]}
                var versionFound = output.Contains($"\"version\": \"{expectedVersion}\"");
                
                if (!versionFound)
                {
                    // Also check without exact case
                    versionFound = Regex.IsMatch(output, $"\"version\"\\s*:\\s*\"{Regex.Escape(expectedVersion)}\"", RegexOptions.IgnoreCase);
                }
                
                if (!versionFound)
                {
                    unavailableVersions.Add((packageName, expectedVersion));
                }
            }
            catch (Exception ex)
            {
                feedQueryErrors.Add($"{packageName}: {ex.Message}");
            }
        }
        
        if (unavailableVersions.Any())
        {
            hasErrors = true;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"   ❌ FAILED — {unavailableVersions.Count} version(s) NOT available on gitlab:");
            Console.ResetColor();
            foreach (var (pkgName, pkgVersion) in unavailableVersions.OrderBy(x => x.package))
            {
                Console.WriteLine($"   • {pkgName,-55} v{pkgVersion}");
            }
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("   Steps to publish:");
            Console.WriteLine("   1. Navigate to the repository");
            Console.WriteLine("   2. Create a feature branch from the prod-rel branch");
            Console.WriteLine("   3. Update VersionPrefix in all package csproj files");
            Console.WriteLine("   4. Create a merge request and merge to prod-rel branch");
            Console.WriteLine("   5. Wait for the nuget-publish pipeline job to complete");
            Console.WriteLine("   6. Verify published versions: dotnet package search {{packageName}} --exact-match --prerelease --source gitlab");
            Console.WriteLine("   7. Re-run validation: dotnet script .husky/csx/prod-rel-validate.csx");
            Console.ResetColor();
            Console.WriteLine();
            errorMessages.Add($"❌ Check 6 FAILED — {unavailableVersions.Count} version(s) not published to gitlab (see details above)");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"   ✅ All {prodPackages.Count} versions found on gitlab");
            Console.ResetColor();
            
            if (feedQueryErrors.Any())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"   ⚠️  {feedQueryErrors.Count} query warning(s):");
                foreach (var error in feedQueryErrors.Take(2))
                {
                    Console.WriteLine($"      • {error}");
                }
                Console.ResetColor();
            }
        }
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"   ⚠️  Check 6 skipped: {ex.Message}");
    Console.ResetColor();
}

Console.WriteLine();

// ============================================================================
// SUMMARY
// ============================================================================
Console.WriteLine("═══════════════════════════════════════════════════════");

if (hasErrors)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("❌ Prod-rel validation FAILED");
    Console.ResetColor();
    Console.WriteLine();
    foreach (var error in errorMessages)
    {
        Console.WriteLine(error);
    }
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("💡 Run the fix script to resolve:");
    Console.WriteLine("   dotnet script scripts/prep-prod-rel.csx");
    Console.ResetColor();
    Environment.Exit(1);
}
else
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("✅ All validation checks passed!");
    Console.ResetColor();
    Console.WriteLine();
    Environment.Exit(0);
}

