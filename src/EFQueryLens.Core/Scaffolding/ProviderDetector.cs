namespace EFQueryLens.Core.Scaffolding;

/// <summary>
/// Detects the EF Core provider (and whether Projectables is in play) for an executable project,
/// purely from files on disk — no assembly is loaded and no database is contacted.
///
/// Detection order, stopping at the first that yields a single provider:
///   1. The <c>*.deps.json</c> next to the built dll (authoritative — includes transitive refs).
///   2. The project's <c>.csproj</c> <c>&lt;PackageReference&gt;</c> entries.
///   3. A source scan for <c>UseSqlServer/UseNpgsql/UseMySql/UseSqlite</c> (disambiguates when
///      more than one provider package is referenced).
/// </summary>
public static class ProviderDetector
{
    public sealed record Result(ProviderKind Provider, bool UseProjectables, string Source);

    private static readonly (string Package, ProviderKind Kind)[] ProviderPackages =
    [
        ("Microsoft.EntityFrameworkCore.SqlServer", ProviderKind.SqlServer),
        ("Npgsql.EntityFrameworkCore.PostgreSQL", ProviderKind.Npgsql),
        ("Pomelo.EntityFrameworkCore.MySql", ProviderKind.MySql),
        ("Microsoft.EntityFrameworkCore.Sqlite", ProviderKind.Sqlite),
    ];

    private static readonly (string Token, ProviderKind Kind)[] ProviderUseCalls =
    [
        ("UseSqlServer", ProviderKind.SqlServer),
        ("UseNpgsql", ProviderKind.Npgsql),
        ("UseMySql", ProviderKind.MySql),
        ("UseSqlite", ProviderKind.Sqlite),
    ];

    public static Result Detect(string assemblyPath, string projectDirectory)
    {
        var depsText = TryReadDepsJson(assemblyPath);
        var csprojText = TryReadCsproj(projectDirectory);

        var useProjectables =
            Contains(depsText, "EntityFrameworkCore.Projectables")
            || Contains(csprojText, "EntityFrameworkCore.Projectables");

        // 1. deps.json — authoritative.
        var fromDeps = MatchProviderPackages(depsText);
        if (fromDeps.Count == 1)
        {
            return new Result(fromDeps[0], useProjectables, "deps.json");
        }

        // 2. csproj package references.
        var fromCsproj = MatchProviderPackages(csprojText);
        if (fromCsproj.Count == 1)
        {
            return new Result(fromCsproj[0], useProjectables, "csproj");
        }

        // 3. Source scan (also disambiguates when deps/csproj listed multiple providers).
        var fromSource = MatchProviderUseCalls(projectDirectory);
        if (fromSource is { } provider)
        {
            useProjectables = useProjectables || SourceMentionsProjectables(projectDirectory);
            return new Result(provider, useProjectables, "source");
        }

        // Multiple candidates but no source disambiguation, or none at all → Unknown.
        return new Result(ProviderKind.Unknown, useProjectables, fromDeps.Count > 1 || fromCsproj.Count > 1 ? "ambiguous" : "none");
    }

    private static List<ProviderKind> MatchProviderPackages(string? text)
    {
        var found = new List<ProviderKind>();
        if (string.IsNullOrEmpty(text))
        {
            return found;
        }

        foreach (var (package, kind) in ProviderPackages)
        {
            if (text.Contains(package, StringComparison.OrdinalIgnoreCase) && !found.Contains(kind))
            {
                found.Add(kind);
            }
        }

        return found;
    }

    private static ProviderKind? MatchProviderUseCalls(string projectDirectory)
    {
        var matches = new HashSet<ProviderKind>();

        foreach (var file in EnumerateSourceFiles(projectDirectory))
        {
            string text;
            try { text = File.ReadAllText(file); }
            catch { continue; }

            foreach (var (token, kind) in ProviderUseCalls)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    matches.Add(kind);
                }
            }

            if (matches.Count > 1)
            {
                return null; // genuinely ambiguous in source — let the caller ask.
            }
        }

        return matches.Count == 1 ? matches.First() : null;
    }

    private static bool SourceMentionsProjectables(string projectDirectory)
    {
        foreach (var file in EnumerateSourceFiles(projectDirectory))
        {
            try
            {
                if (File.ReadAllText(file).Contains("UseProjectables", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch { /* ignore unreadable file */ }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string projectDirectory)
    {
        if (!Directory.Exists(projectDirectory))
        {
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            // Skip build output and our own generated factory.
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return file;
        }
    }

    private static string? TryReadDepsJson(string assemblyPath)
    {
        try
        {
            var deps = Path.ChangeExtension(assemblyPath, ".deps.json");
            return File.Exists(deps) ? File.ReadAllText(deps) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadCsproj(string projectDirectory)
    {
        try
        {
            if (!Directory.Exists(projectDirectory))
            {
                return null;
            }

            var csproj = Directory.EnumerateFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(f => !f.Contains("Backup", StringComparison.OrdinalIgnoreCase));

            return csproj is not null ? File.ReadAllText(csproj) : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool Contains(string? text, string value)
        => text is not null && text.Contains(value, StringComparison.OrdinalIgnoreCase);
}
