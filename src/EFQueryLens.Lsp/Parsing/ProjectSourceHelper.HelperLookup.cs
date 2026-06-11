using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using EFQueryLens.Core.Scaffolding;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

internal static partial class ProjectSourceHelper
{
    private static readonly ConcurrentDictionary<string, CachedSearchRoots> SearchRootsCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, CachedMethodIndex> MethodIndexCache =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed record CachedSearchRoots(IReadOnlyList<string> ProjectDirectories, long ExpiresAtUtcTicks);

    private sealed record CachedMethodIndex(
        IReadOnlyDictionary<string, string[]> MethodToFiles,
        long ExpiresAtUtcTicks);

    private static readonly long SearchRootsTtlTicks = TimeSpan.FromMinutes(2).Ticks;

    /// <summary>
    /// Resolves Roslyn roots for helper-method synthesis: current file is searched by the caller;
    /// this returns 0–N additional project directories to scan (referenced projects first).
    /// </summary>
    internal static IReadOnlyList<SyntaxNode> TryResolveHelperMethodRoots(
        string sourceFilePath,
        string sourceText,
        string methodName,
        string? receiverTypeName)
    {
        var candidateFiles = new List<string>();

        if (!string.IsNullOrWhiteSpace(receiverTypeName))
        {
            candidateFiles.AddRange(FindTypeDefinitionFiles(sourceFilePath, receiverTypeName));
        }

        candidateFiles.AddRange(FindFilesDeclaringMethod(sourceFilePath, methodName));

        var normalizedSource = Path.GetFullPath(sourceFilePath);
        var roots = new List<SyntaxNode>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalizedSource };

        foreach (var file in candidateFiles.Distinct(StringComparer.OrdinalIgnoreCase).Take(12))
        {
            if (!seen.Add(file) || !File.Exists(file))
            {
                continue;
            }

            try
            {
                roots.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot());
            }
            catch
            {
                // skip unreadable
            }
        }

        return roots;
    }

    internal static IReadOnlyList<string> GetSearchProjectDirectories(string sourceFilePath)
    {
        var projectDir = AssemblyResolver.TryGetProjectDirectory(sourceFilePath);
        if (string.IsNullOrWhiteSpace(projectDir))
        {
            return [];
        }

        var now = DateTime.UtcNow.Ticks;
        if (SearchRootsCache.TryGetValue(projectDir, out var cached) && cached.ExpiresAtUtcTicks > now)
        {
            return cached.ProjectDirectories;
        }

        // Search referenced projects first so call-site files in the current project
        // do not crowd out real method definitions in Core/service layers.
        var directories = new List<string>();
        directories.AddRange(ParseDirectProjectReferenceDirectories(projectDir));
        if (!directories.Contains(projectDir, StringComparer.OrdinalIgnoreCase))
        {
            directories.Add(projectDir);
        }

        var slnFile = SolutionFileResolver.FindSolutionFile(projectDir);
        if (slnFile is not null)
        {
            foreach (var csproj in SolutionFileResolver.ParseSolutionProjects(slnFile))
            {
                var dir = Path.GetDirectoryName(csproj);
                if (!string.IsNullOrWhiteSpace(dir)
                    && !directories.Contains(dir, StringComparer.OrdinalIgnoreCase))
                {
                    directories.Add(dir);
                }
            }
        }

        SearchRootsCache[projectDir] = new CachedSearchRoots(directories, now + SearchRootsTtlTicks);
        return directories;
    }

    private static IEnumerable<string> ParseDirectProjectReferenceDirectories(string projectDir)
    {
        foreach (var csproj in Directory.GetFiles(projectDir, "*.csproj", SearchOption.TopDirectoryOnly))
        {
            string text;
            try
            {
                text = File.ReadAllText(csproj);
            }
            catch
            {
                continue;
            }

            foreach (Match match in ProjectReferenceRegex().Matches(text))
            {
                var include = match.Groups["include"].Value.Trim().Replace('\\', Path.DirectorySeparatorChar);
                var referenced = Path.GetFullPath(Path.Combine(projectDir, include));
                var referencedDir = Path.GetDirectoryName(referenced);
                if (!string.IsNullOrWhiteSpace(referencedDir) && Directory.Exists(referencedDir))
                {
                    yield return referencedDir;
                }
            }
        }
    }

    private static IEnumerable<string> FindTypeDefinitionFiles(string sourceFilePath, string typeName)
    {
        var normalizedType = NormalizeTypeLookupName(typeName);
        if (string.IsNullOrWhiteSpace(normalizedType))
        {
            yield break;
        }

        foreach (var projectDir in GetSearchProjectDirectories(sourceFilePath))
        {
            var byName = Path.Combine(projectDir, $"{normalizedType}.cs");
            if (File.Exists(byName) && !IsBuildOutputPath(byName))
            {
                yield return byName;
            }

            foreach (var file in EnumerateCandidateSourceFiles(projectDir))
            {
                if (FileContainsTypeDeclaration(file, normalizedType))
                {
                    yield return file;
                }
            }
        }
    }

    private static IEnumerable<string> FindFilesDeclaringMethod(string sourceFilePath, string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            yield break;
        }

        foreach (var projectDir in GetSearchProjectDirectories(sourceFilePath))
        {
            var index = GetOrBuildMethodIndex(projectDir);
            if (!index.TryGetValue(methodName, out var files))
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private static IReadOnlyDictionary<string, string[]> GetOrBuildMethodIndex(string projectDir)
    {
        var now = DateTime.UtcNow.Ticks;
        if (MethodIndexCache.TryGetValue(projectDir, out var cached) && cached.ExpiresAtUtcTicks > now)
        {
            return cached.MethodToFiles;
        }

        var methodToFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in EnumerateCandidateSourceFiles(projectDir))
        {
            foreach (var declaredMethod in GetDeclaredMethodNames(file))
            {
                if (!methodToFiles.TryGetValue(declaredMethod, out var files))
                {
                    files = [];
                    methodToFiles[declaredMethod] = files;
                }

                files.Add(file);
            }
        }

        var frozen = methodToFiles.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
        MethodIndexCache[projectDir] = new CachedMethodIndex(frozen, now + SearchRootsTtlTicks);
        return frozen;
    }

    private static IEnumerable<string> GetDeclaredMethodNames(string filePath)
    {
        string text;
        try
        {
            text = File.ReadAllText(filePath);
        }
        catch
        {
            yield break;
        }

        SyntaxNode root;
        try
        {
            root = CSharpSyntaxTree.ParseText(text).GetRoot();
        }
        catch
        {
            yield break;
        }

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var name = method.Identifier.Text;
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return name;
            }
        }
    }

    private static IEnumerable<string> EnumerateCandidateSourceFiles(string projectDir)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        var count = 0;
        foreach (var file in files)
        {
            if (IsBuildOutputPath(file))
            {
                continue;
            }

            yield return file;
            if (++count >= MaxFilesPerProject)
            {
                yield break;
            }
        }
    }

    private static bool FileContainsTypeDeclaration(string filePath, string typeName)
    {
        string text;
        try
        {
            text = File.ReadAllText(filePath);
        }
        catch
        {
            return false;
        }

        return TypeDeclarationRegex(typeName).IsMatch(text);
    }

    private static bool FileDeclaresMethod(string filePath, string methodName)
    {
        string text;
        try
        {
            text = File.ReadAllText(filePath);
        }
        catch
        {
            return false;
        }

        try
        {
            var root = CSharpSyntaxTree.ParseText(text).GetRoot();
            return root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Any(method => string.Equals(method.Identifier.Text, methodName, StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeTypeLookupName(string typeName)
    {
        var trimmed = typeName.Trim();
        if (trimmed.StartsWith("I", StringComparison.Ordinal) && trimmed.Length > 2 && char.IsUpper(trimmed[1]))
        {
            return trimmed[1..];
        }

        return trimmed;
    }

    private static bool IsBuildOutputPath(string filePath)
    {
        var normalized = filePath.Replace('/', Path.DirectorySeparatorChar);
        return normalized.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"<ProjectReference\s+Include\s*=\s*""(?<include>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProjectReferenceRegex();

    private static Regex TypeDeclarationRegex(string typeName) =>
        new($@"\b(class|record|struct|interface)\s+{Regex.Escape(typeName)}\b", RegexOptions.CultureInvariant);

}
