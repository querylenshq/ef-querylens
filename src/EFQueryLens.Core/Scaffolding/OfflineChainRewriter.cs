using System.Text.RegularExpressions;

namespace EFQueryLens.Core.Scaffolding;

/// <summary>
/// Rewrites a captured <c>AddDbContext</c> options chain for offline SQL preview by replacing
/// only the provider call's connection-string argument with a placeholder literal.
/// </summary>
public static class OfflineChainRewriter
{
    private static readonly (string Token, ProviderKind Kind)[] ProviderUseCalls =
    [
        ("UseSqlServer", ProviderKind.SqlServer),
        ("UseNpgsql", ProviderKind.Npgsql),
        ("UseMySql", ProviderKind.MySql),
        ("UseSqlite", ProviderKind.Sqlite),
    ];

    public sealed record Result(
        string OfflineChain,
        ProviderKind Provider,
        bool UseProjectables,
        bool UseSplitQuery,
        IReadOnlyList<string> RequiredUsings);

    public static Result Rewrite(DbContextRegistration registration)
    {
        var chain = registration.OptionsChain.StartsWith(".", StringComparison.Ordinal)
            ? registration.OptionsChain
            : "." + registration.OptionsChain;

        var provider = DetectProvider(chain);
        var offlineChain = ReplaceConnectionStringArgument(chain, provider);
        var useProjectables = chain.Contains("UseProjectables", StringComparison.Ordinal);
        var useSplitQuery = chain.Contains("UseQuerySplittingBehavior", StringComparison.Ordinal)
                            && chain.Contains("SplitQuery", StringComparison.Ordinal);

        var requiredUsings = CollectRequiredUsings(registration.Usings, chain, useProjectables);

        return new Result(
            offlineChain,
            provider,
            useProjectables,
            useSplitQuery,
            requiredUsings);
    }

    public static Result RewriteChain(
        string optionsChain,
        IReadOnlyList<string> usings,
        ProviderKind providerFallback = ProviderKind.Unknown)
    {
        var chain = optionsChain.StartsWith(".", StringComparison.Ordinal)
            ? optionsChain
            : "." + optionsChain;

        var provider = DetectProvider(chain);
        if (provider == ProviderKind.Unknown)
        {
            provider = providerFallback;
        }

        var offlineChain = provider == ProviderKind.Unknown
            ? chain
            : ReplaceConnectionStringArgument(chain, provider);

        var useProjectables = chain.Contains("UseProjectables", StringComparison.Ordinal);
        var useSplitQuery = chain.Contains("UseQuerySplittingBehavior", StringComparison.Ordinal)
                            && chain.Contains("SplitQuery", StringComparison.Ordinal);

        return new Result(
            offlineChain,
            provider,
            useProjectables,
            useSplitQuery,
            CollectRequiredUsings(usings, chain, useProjectables));
    }

    internal static ProviderKind DetectProvider(string chain)
    {
        foreach (var (token, kind) in ProviderUseCalls)
        {
            if (chain.Contains(token, StringComparison.Ordinal))
            {
                return kind;
            }
        }

        return ProviderKind.Unknown;
    }

    internal static string ReplaceConnectionStringArgument(string chain, ProviderKind provider)
    {
        foreach (var (token, kind) in ProviderUseCalls)
        {
            if (kind != provider)
            {
                continue;
            }

            var pattern = "\\." + token + "\\s*\\(";
            var match = Regex.Match(chain, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
            if (!match.Success)
            {
                continue;
            }

            var openParenIndex = match.Index + match.Length - 1;
            if (!TryFindFirstArgumentSpan(chain, openParenIndex, out var argStart, out var argEnd))
            {
                continue;
            }

            var placeholder = OfflineConnectionString(provider);
            return chain[..argStart] + placeholder + chain[argEnd..];
        }

        return chain;
    }

    private static bool TryFindFirstArgumentSpan(string text, int openParenIndex, out int argStart, out int argEnd)
    {
        argStart = 0;
        argEnd = 0;

        var index = openParenIndex + 1;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        if (index >= text.Length)
        {
            return false;
        }

        argStart = index;
        var depth = 0;
        var inString = false;
        var stringChar = '\0';

        for (; index < text.Length; index++)
        {
            var ch = text[index];

            if (inString)
            {
                if (ch == '\\')
                {
                    index++;
                    continue;
                }

                if (ch == stringChar)
                {
                    inString = false;
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                case '\'':
                    inString = true;
                    stringChar = ch;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    if (depth == 0)
                    {
                        argEnd = index;
                        return argEnd > argStart;
                    }

                    depth--;
                    break;
                case ',':
                    if (depth == 0)
                    {
                        argEnd = index;
                        return argEnd > argStart;
                    }

                    break;
            }
        }

        return false;
    }

    internal static string OfflineConnectionString(ProviderKind provider)
        => provider switch
        {
            ProviderKind.SqlServer =>
                "\"Server=ef_querylens_offline;Database=ef_querylens_offline;User Id=ef_querylens_offline;Password=ef_querylens_offline;TrustServerCertificate=True\"",
            ProviderKind.Npgsql =>
                "\"Host=ef_querylens_offline;Database=ef_querylens_offline;Username=ef_querylens_offline;Password=ef_querylens_offline\"",
            ProviderKind.MySql =>
                "\"Server=ef_querylens_offline;Database=ef_querylens_offline;User Id=ef_querylens_offline;Password=ef_querylens_offline\"",
            ProviderKind.Sqlite =>
                "\"Data Source=:memory:\"",
            _ => "\"\"",
        };

    private static IReadOnlyList<string> CollectRequiredUsings(
        IReadOnlyList<string> fileUsings,
        string chain,
        bool useProjectables)
    {
        var required = new HashSet<string>(StringComparer.Ordinal)
        {
            "Microsoft.EntityFrameworkCore",
        };

        if (useProjectables)
        {
            required.Add("EntityFrameworkCore.Projectables");
        }

        if (chain.Contains("MySqlServerVersion", StringComparison.Ordinal)
            || chain.Contains("UseMySql", StringComparison.Ordinal))
        {
            required.Add("Microsoft.EntityFrameworkCore");
        }

        if (chain.Contains("QuerySplittingBehavior", StringComparison.Ordinal))
        {
            required.Add("Microsoft.EntityFrameworkCore");
        }

        var selected = new List<string>();
        foreach (var candidate in fileUsings)
        {
            if (required.Contains(candidate))
            {
                selected.Add(candidate);
            }
        }

        foreach (var requiredUsing in required)
        {
            if (!selected.Contains(requiredUsing, StringComparer.Ordinal))
            {
                selected.Add(requiredUsing);
            }
        }

        return selected.OrderBy(static u => u, StringComparer.Ordinal).ToList();
    }
}
