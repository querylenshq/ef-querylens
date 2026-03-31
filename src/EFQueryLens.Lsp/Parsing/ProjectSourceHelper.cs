using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace EFQueryLens.Lsp.Parsing;

/// <summary>
/// Provides a short-lived cache of parsed SyntaxNode roots for all .cs files in a project,
/// used for cross-file method declaration lookup (e.g. Expression parameter helper synthesis).
/// </summary>
internal static class ProjectSourceHelper
{
    private sealed record CachedProjectIndex(ProjectSourceIndex Index, long ExpiresAtUtcTicks);

    private static readonly ConcurrentDictionary<string, CachedProjectIndex> IndexCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly long TtlTicks = TimeSpan.FromSeconds(10).Ticks;

    // Hard cap on the number of source files we index per project.
    private const int MaxFilesPerProject = 500;

    /// <summary>
    /// Returns a <see cref="ProjectSourceIndex"/> for the project that contains
    /// <paramref name="currentFilePath"/>, including source files from directly
    /// referenced projects (one level of <c>ProjectReference</c> deep).
    /// The index is cached for 10 seconds; file parsing happens lazily, only when
    /// <see cref="ProjectSourceIndex.FindRootsForMethod"/> is called.
    /// </summary>
    public static ProjectSourceIndex GetProjectIndex(string currentFilePath)
    {
        var projectDir = AssemblyResolver.TryGetProjectDirectory(currentFilePath);
        if (string.IsNullOrEmpty(projectDir))
            return new ProjectSourceIndex([], currentFilePath);

        var now = DateTime.UtcNow.Ticks;
        if (IndexCache.TryGetValue(projectDir, out var cached) && cached.ExpiresAtUtcTicks > now)
            return cached.Index;

        var normalizedCurrentPath = Path.GetFullPath(currentFilePath);
        var paths = new List<string>();

        // Files in the same project
        foreach (var file in Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories))
        {
            if (paths.Count >= MaxFilesPerProject)
                break;

            if (string.Equals(Path.GetFullPath(file), normalizedCurrentPath, StringComparison.OrdinalIgnoreCase))
                continue; // caller already holds this as the primary root

            paths.Add(file);
        }

        // Files from directly referenced projects (one level deep)
        var referencedDirs = AssemblyResolver.TryGetProjectReferenceDirs(projectDir);
        foreach (var refDir in referencedDirs)
        {
            foreach (var file in Directory.GetFiles(refDir, "*.cs", SearchOption.AllDirectories))
            {
                if (paths.Count >= MaxFilesPerProject)
                    break;

                var relativePart = file[(refDir.Length + 1)..];
                if (relativePart.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || relativePart.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;

                paths.Add(file);
            }

            if (paths.Count >= MaxFilesPerProject)
                break;
        }

        var index = new ProjectSourceIndex(paths, currentFilePath);
        IndexCache[projectDir] = new CachedProjectIndex(index, now + TtlTicks);
        return index;
    }
}

/// <summary>
/// A project-scoped index of source file paths that parses files on demand,
/// filtered by method name.  A single instance is shared across all usages
/// within one hover request window (via the <see cref="ProjectSourceHelper"/> cache).
/// </summary>
public sealed class ProjectSourceIndex
{
    private readonly IReadOnlyList<string> _filePaths;

    // Cache parsed roots per method name so repeated lookups in the same hover window
    // (e.g. semantic context + pipeline) don't re-parse.
    private readonly ConcurrentDictionary<string, IReadOnlyList<SyntaxNode>> _methodCache =
        new(StringComparer.Ordinal);

    /// <summary>Total number of candidate files in this index.</summary>
    public int FileCount => _filePaths.Count;

    internal ProjectSourceIndex(IReadOnlyList<string> filePaths, string currentFilePath = "")
    {
        _filePaths = filePaths;
    }

    /// <summary>
    /// Returns parsed <see cref="SyntaxNode"/> roots for all files in this index that
    /// contain <paramref name="methodName"/> as a plain-text substring.  Only those
    /// files are read and parsed — files that do not mention the name at all are skipped
    /// entirely, keeping this path fast even for large codebases.
    /// </summary>
    public IReadOnlyList<SyntaxNode> FindRootsForMethod(string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
            return [];

        return _methodCache.GetOrAdd(methodName, name =>
        {
            var roots = new List<SyntaxNode>();
            foreach (var path in _filePaths)
            {
                string text;
                try
                {
                    text = File.ReadAllText(path);
                }
                catch
                {
                    continue;
                }

                // Fast pre-filter: skip files that don't mention the method name at all.
                if (!text.Contains(name, StringComparison.Ordinal))
                    continue;

                try
                {
                    roots.Add(CSharpSyntaxTree.ParseText(text).GetRoot());
                }
                catch
                {
                    // unparseable file — skip silently
                }
            }

            return roots;
        });
    }
}
