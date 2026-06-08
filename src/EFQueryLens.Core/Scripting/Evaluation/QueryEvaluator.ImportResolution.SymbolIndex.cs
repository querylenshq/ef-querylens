using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace EFQueryLens.Core.Scripting.Evaluation;

public sealed partial class QueryEvaluator
{
    private static readonly ConcurrentDictionary<string, bool> SUsingNameValidity =
        new(StringComparer.Ordinal);

    private static (HashSet<string> Namespaces, HashSet<string> Types) BuildKnownNamespaceAndTypeIndex(
        IEnumerable<Assembly> assemblies)
    {
        var ns = new HashSet<string>(StringComparer.Ordinal);
        var types = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asm in assemblies)
        {
            var key = string.IsNullOrWhiteSpace(asm.Location)
                ? asm.FullName ?? Guid.NewGuid().ToString("N")
                : asm.Location;
            if (!seen.Add(key))
                continue;

            Type[] all;
            try
            {
                all = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException rtle)
            {
                all = rtle.Types.Where(t => t is not null).Cast<Type>().ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var t in all)
            {
                if (!string.IsNullOrWhiteSpace(t.FullName))
                    types.Add(t.FullName.Replace('+', '.'));

                if (!string.IsNullOrWhiteSpace(t.Namespace))
                    AddNamespaceAndParents(t.Namespace, ns);
            }
        }

        return (ns, types);
    }

    private static void AddNamespaceAndParents(string n, ISet<string> dest)
    {
        var span = n.AsSpan();
        while (true)
        {
            dest.Add(span.ToString());
            var dot = span.LastIndexOf('.');
            if (dot <= 0)
                break;

            span = span[..dot];
        }
    }

    /// <summary>
    /// Returns the distinct namespaces of all types in <paramref name="knownTypes"/> whose
    /// simple name (last segment of the fully-qualified name) matches <paramref name="simpleName"/>.
    /// Used to synthesise <c>using</c> directives when CS0246 fires because a DTO or result type
    /// lives in the same namespace as the calling class and therefore has no explicit import.
    /// </summary>
    internal static IEnumerable<string> FindNamespacesForSimpleName(
        string simpleName,
        IReadOnlySet<string> knownTypes)
    {
        var suffix = "." + simpleName;
        return knownTypes
            .Where(fqn => fqn.EndsWith(suffix, StringComparison.Ordinal))
            .Select(fqn =>
            {
                var lastDot = fqn.LastIndexOf('.');
                return lastDot > 0 ? fqn[..lastDot] : null;
            })
            .Where(ns => ns is not null)
            .Distinct(StringComparer.Ordinal)!;
    }

    /// <summary>
    /// Infers <c>using Prefix = Namespace;</c> when <paramref name="prefix"/> is used as a
    /// namespace alias in the expression (e.g. <c>Domain.Enums.ApplicationType</c>) but the alias
    /// was not supplied by the LSP.
    /// </summary>
    internal static bool TryInferUsingAliasForNamespacePrefix(
        string prefix,
        string expression,
        IReadOnlySet<string> knownTypes,
        out string aliasTarget)
    {
        aliasTarget = string.Empty;

        if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(expression))
            return false;

        var pattern = $@"(?<!\w){Regex.Escape(prefix)}\.((?:\w+\.)*\w+)";
        string? bestTarget = null;
        var bestScore = int.MinValue;

        foreach (Match match in Regex.Matches(expression, pattern))
        {
            var qualifiedTail = match.Groups[1].Value;
            var segments = qualifiedTail.Split('.');

            for (var len = segments.Length; len >= 1; len--)
            {
                var typeSuffix = string.Join('.', segments.AsSpan(0, len));
                var dottedSuffix = "." + typeSuffix;

                foreach (var fqn in knownTypes)
                {
                    if (!fqn.EndsWith(dottedSuffix, StringComparison.Ordinal))
                        continue;

                    var candidate = fqn[..^dottedSuffix.Length];
                    if (string.IsNullOrWhiteSpace(candidate))
                        continue;

                    var score = ScoreAliasTarget(prefix, candidate);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTarget = candidate;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(bestTarget))
            return false;

        aliasTarget = bestTarget;
        return true;
    }

    private static int ScoreAliasTarget(string prefix, string candidateNamespace)
    {
        var score = 0;

        if (candidateNamespace.EndsWith("." + prefix, StringComparison.Ordinal))
            score += 100;

        var lastSegment = candidateNamespace.Contains('.')
            ? candidateNamespace[(candidateNamespace.LastIndexOf('.') + 1)..]
            : candidateNamespace;

        if (string.Equals(lastSegment, prefix, StringComparison.Ordinal))
            score += 50;

        if (candidateNamespace.Contains(prefix, StringComparison.Ordinal))
            score += 10;

        return score;
    }

    private static bool IsResolvableNamespace(string n, IReadOnlySet<string> ns) => ns.Contains(n);

    private static bool IsResolvableType(string n, IReadOnlySet<string> types) => types.Contains(n);

    private static bool IsResolvableTypeOrNamespace(
        string n,
        IReadOnlySet<string> ns,
        IReadOnlySet<string> types) =>
        ns.Contains(n) || types.Contains(n);

    private static bool IsValidAliasName(string a) =>
        !string.IsNullOrWhiteSpace(a) && SyntaxFacts.IsValidIdentifier(a);

    private static bool IsValidUsingName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return SUsingNameValidity.GetOrAdd(name, static candidate =>
        {
            if (LooksLikeQualifiedIdentifier(candidate))
                return true;

            return !CSharpSyntaxTree.ParseText($"using {candidate};")
                .GetDiagnostics()
                .Any(d => d.Severity == DiagnosticSeverity.Error);
        });
    }

    private static bool LooksLikeQualifiedIdentifier(string name)
    {
        var candidate = name.Trim();
        if (candidate.StartsWith("global::", StringComparison.Ordinal))
        {
            candidate = candidate["global::".Length..];
        }

        if (candidate.Length == 0)
            return false;

        var segmentStart = 0;
        while (segmentStart < candidate.Length)
        {
            var dotIndex = candidate.IndexOf('.', segmentStart);
            var segmentLength = dotIndex < 0
                ? candidate.Length - segmentStart
                : dotIndex - segmentStart;
            if (segmentLength <= 0)
                return false;

            var segment = candidate.Substring(segmentStart, segmentLength);
            if (!SyntaxFacts.IsValidIdentifier(segment))
                return false;

            if (dotIndex < 0)
                return true;

            segmentStart = dotIndex + 1;
        }

        return false;
    }
}
