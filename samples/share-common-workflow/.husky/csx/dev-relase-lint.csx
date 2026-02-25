using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;

var currentDirectory = Directory.GetCurrentDirectory();
var packagesPropsFile = Path.Combine(currentDirectory, "Directory.Packages.props");

// Regex pattern to match PackageVersion entries with Share.*-dev or Share.*-sit
string shareDevPattern = @"<PackageVersion\s+Include=""(Share\..*)"">\s+Version=""(.*-(dev|sit)[^""]*)""\s*/>";

bool hasShareDevVersion(string filePath, out List<string> matchingPackages)
{
    matchingPackages = new List<string>();

    if (!File.Exists(filePath))
    {
        Console.WriteLine($"File {filePath} not found.");
        return false;
    }

    string content = File.ReadAllText(filePath);
    var matches = Regex.Matches(content, shareDevPattern);

    foreach (Match match in matches)
    {
        if (match.Success)
        {
            string packageName = match.Groups[1].Value;
            string packageVersion = match.Groups[2].Value;
            matchingPackages.Add($"{packageName} - {packageVersion}");
        }
    }

    return matchingPackages.Count > 0;
}

// Check for Share.*-dev or Share.*-sit versions in Directory.Packages.props
if (hasShareDevVersion(packagesPropsFile, out var matchingPackages))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("Error: The following Share.* pre-release versions (-dev or -sit) were detected in Directory.Packages.props:");
    foreach (var package in matchingPackages)
    {
        Console.WriteLine(package);
    }
    Console.ResetColor();
    return 1;
}

Console.WriteLine("No Share.*-dev versions detected in Directory.Packages.props. Proceeding with push.");
return 0;
