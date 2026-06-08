using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EFQueryLens.Core.Scaffolding;

/// <summary>
/// Discovers and parses Visual Studio solution files (.slnx preferred over legacy .sln).
/// </summary>
public static class SolutionFileResolver
{
    public static string? FindSolutionFile(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return null;
        }

        var dir = Path.GetFullPath(startDirectory);
        while (!string.IsNullOrEmpty(dir))
        {
            var selected = SelectSolutionFileInDirectory(dir);
            if (selected is not null)
            {
                return selected;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    public static IReadOnlyList<string> ParseSolutionProjects(string solutionFilePath)
    {
        if (string.IsNullOrWhiteSpace(solutionFilePath) || !File.Exists(solutionFilePath))
        {
            return [];
        }

        return solutionFilePath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ParseSlnxProjects(solutionFilePath)
            : ParseLegacySlnProjects(solutionFilePath);
    }

    internal static string? SelectSolutionFileInDirectory(string directory)
    {
        string[] slnxFiles;
        string[] slnFiles;

        try
        {
            slnxFiles = Directory.GetFiles(directory, "*.slnx");
            slnFiles = Directory.GetFiles(directory, "*.sln");
        }
        catch
        {
            return null;
        }

        if (slnxFiles.Length > 0)
        {
            return PickBestSolutionFile(directory, slnxFiles);
        }

        if (slnFiles.Length > 0)
        {
            return PickBestSolutionFile(directory, slnFiles);
        }

        return null;
    }

    private static string PickBestSolutionFile(string directory, string[] candidates)
    {
        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        var directoryName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var nameMatch = candidates
            .Where(path => string.Equals(
                Path.GetFileNameWithoutExtension(path),
                directoryName,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return nameMatch ?? candidates.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).First();
    }

    private static List<string> ParseLegacySlnProjects(string slnFile)
    {
        var content = File.ReadAllText(slnFile);
        var slnDir = Path.GetDirectoryName(slnFile)!;
        var projects = new List<string>();

        foreach (Match match in Regex.Matches(
                     content,
                     @"Project\("".+?""\)\s*=\s*"".+?""\s*,\s*""(.+?\.csproj)""",
                     RegexOptions.Multiline,
                     TimeSpan.FromSeconds(5)))
        {
            var projectPath = Path.GetFullPath(Path.Combine(slnDir, match.Groups[1].Value));
            if (File.Exists(projectPath))
            {
                projects.Add(projectPath);
            }
        }

        return projects;
    }

    private static List<string> ParseSlnxProjects(string slnxFile)
    {
        var slnDir = Path.GetDirectoryName(slnxFile)!;
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var document = XDocument.Load(slnxFile);
            foreach (var projectElement in document.Descendants().Where(e => e.Name.LocalName == "Project"))
            {
                var pathAttribute = projectElement.Attribute("Path")?.Value;
                if (string.IsNullOrWhiteSpace(pathAttribute)
                    || !pathAttribute.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var normalizedRelative = pathAttribute.Replace('/', Path.DirectorySeparatorChar);
                var projectPath = Path.GetFullPath(Path.Combine(slnDir, normalizedRelative));
                if (File.Exists(projectPath))
                {
                    projects.Add(projectPath);
                }
            }
        }
        catch
        {
            return [];
        }

        return projects.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
