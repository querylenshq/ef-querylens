using System.Text;

namespace EFQueryLens.Core.Scaffolding;

/// <summary>
/// Idempotently ensures a <c>.gitignore</c> rule exists so the generated factory is never
/// committed. Creates the file if absent; appends the rule (with a marker comment) only when not
/// already present.
/// </summary>
public static class GitignoreWriter
{
    private const string MarkerComment = "# EF QueryLens (generated locally for SQL preview; not committed)";

    /// <summary>
    /// Ensures <paramref name="rule"/> (e.g. <c>Properties/QueryLens/</c>) is present in the
    /// <c>.gitignore</c> in <paramref name="directory"/>. Returns true if the file was created or
    /// modified, false if the rule was already present.
    /// </summary>
    public static bool EnsureRule(string directory, string rule)
    {
        var path = Path.Combine(directory, ".gitignore");

        if (!File.Exists(path))
        {
            File.WriteAllText(path, MarkerComment + "\n" + rule + "\n");
            return true;
        }

        var existing = File.ReadAllText(path);
        if (HasRule(existing, rule))
        {
            return false;
        }

        var sb = new StringBuilder(existing);
        if (existing.Length > 0 && !existing.EndsWith('\n') && !existing.EndsWith('\r'))
        {
            sb.Append('\n');
        }

        sb.Append('\n').Append(MarkerComment).Append('\n').Append(rule).Append('\n');
        File.WriteAllText(path, sb.ToString());
        return true;
    }

    private static bool HasRule(string content, string rule)
    {
        var normalizedRule = rule.Replace('\\', '/').Trim();
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Replace('\\', '/').Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (string.Equals(line.TrimStart('/'), normalizedRule.TrimStart('/'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
