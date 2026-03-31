using EFQueryLens.Core.Contracts;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

public static partial class LspSyntaxHelper
{
    /// <summary>
    /// Scans <paramref name="sourceText"/> for a field, local variable, or parameter declaration
    /// whose name matches <paramref name="contextVariableName"/> and returns the declared type
    /// name string — suitable for populating <c>TranslationRequest.DbContextTypeName</c> to
    /// disambiguate when multiple DbContext types exist in the host assembly.
    ///
    /// Returns <c>null</c> when the variable cannot be found or its type cannot be determined
    /// syntactically (e.g. <c>var</c> with a complex initializer).
    /// </summary>
    internal static string? TryResolveDbContextTypeName(string sourceText, string contextVariableName)
    {
        if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(contextVariableName))
            return null;

        try
        {
            var root = CSharpSyntaxTree.ParseText(sourceText).GetRoot();

            string? resolved = null;

            // Fields: private readonly SlaPlusDbContext _db;
            foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            {
                if (field.Declaration.Variables.Any(v =>
                        v.Identifier.ValueText.Equals(contextVariableName, StringComparison.Ordinal)))
                {
                    resolved = field.Declaration.Type.ToString();
                    break;
                }
            }

            // Locals: var db = ...; SlaPlusDbContext db = ...;
            if (resolved is null)
            {
                foreach (var local in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
                {
                    if (local.Declaration.Variables.Any(v =>
                            v.Identifier.ValueText.Equals(contextVariableName, StringComparison.Ordinal)))
                    {
                        resolved = local.Declaration.Type.ToString();
                        break;
                    }
                }
            }

            // Parameters: (SlaPlusDbContext db) or injected via ctor
            if (resolved is null)
            {
                foreach (var parameter in root.DescendantNodes().OfType<ParameterSyntax>())
                {
                    if (parameter.Identifier.ValueText.Equals(contextVariableName, StringComparison.Ordinal)
                        && parameter.Type is not null)
                    {
                        resolved = parameter.Type.ToString();
                        break;
                    }
                }
            }

            return resolved is not null ? NormalizeDbContextTypeName(resolved) : null;
        }
        catch
        {
            // Best-effort — never propagate to caller.
        }

        return null;
    }

    /// <summary>
    /// Normalises a syntactically-resolved type name for use as a DbContext disambiguator.
    /// Strips nullable-reference-type annotations (<c>?</c>) — they have no CLR distinction.
    /// </summary>
    private static string NormalizeDbContextTypeName(string typeName) => typeName.TrimEnd('?');

    internal static DbContextResolutionSnapshot? BuildDbContextResolutionSnapshot(
        string sourceText,
        string contextVariableName,
        IReadOnlyList<string>? factoryCandidateTypeNames)
    {
        var declaredTypeName = TryResolveDbContextTypeName(sourceText, contextVariableName);
        var normalizedFactoryCandidates = (factoryCandidateTypeNames ?? [])
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(NormalizeDbContextTypeName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var factoryTypeName = normalizedFactoryCandidates.Length == 1
            ? normalizedFactoryCandidates[0]
            : null;

        if (declaredTypeName is null && factoryTypeName is null && normalizedFactoryCandidates.Length == 0)
            return null;

        return new DbContextResolutionSnapshot
        {
            DeclaredTypeName = declaredTypeName,
            FactoryTypeName = factoryTypeName,
            FactoryCandidateTypeNames = normalizedFactoryCandidates,
            ResolutionSource = BuildResolutionSource(declaredTypeName, factoryTypeName, normalizedFactoryCandidates),
            Confidence = ComputeResolutionConfidence(declaredTypeName, factoryTypeName, normalizedFactoryCandidates),
        };
    }

    internal static string? GetPreferredDbContextTypeName(DbContextResolutionSnapshot? snapshot)
    {
        if (snapshot is null)
            return null;

        if (!string.IsNullOrWhiteSpace(snapshot.DeclaredTypeName))
            return snapshot.DeclaredTypeName;

        if (!string.IsNullOrWhiteSpace(snapshot.FactoryTypeName))
            return snapshot.FactoryTypeName;

        return snapshot.FactoryCandidateTypeNames.Count == 1
            ? snapshot.FactoryCandidateTypeNames[0]
            : null;
    }

    internal static string GetDbContextResolutionCacheToken(DbContextResolutionSnapshot? snapshot)
    {
        if (snapshot is null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(snapshot.DeclaredTypeName))
            return snapshot.DeclaredTypeName;

        if (!string.IsNullOrWhiteSpace(snapshot.FactoryTypeName))
            return snapshot.FactoryTypeName;

        if (snapshot.FactoryCandidateTypeNames.Count == 0)
            return string.Empty;

        return string.Join(";", snapshot.FactoryCandidateTypeNames.Order(StringComparer.Ordinal));
    }

    private static string BuildResolutionSource(
        string? declaredTypeName,
        string? factoryTypeName,
        IReadOnlyList<string> factoryCandidateTypeNames)
    {
        if (!string.IsNullOrWhiteSpace(declaredTypeName) && !string.IsNullOrWhiteSpace(factoryTypeName))
        {
            return string.Equals(declaredTypeName, factoryTypeName, StringComparison.Ordinal)
                ? "declared+factory"
                : "declared+factory-mismatch";
        }

        if (!string.IsNullOrWhiteSpace(declaredTypeName) && factoryCandidateTypeNames.Count > 1)
            return "declared+factory-candidates";

        if (!string.IsNullOrWhiteSpace(declaredTypeName))
            return "declared";

        if (!string.IsNullOrWhiteSpace(factoryTypeName))
            return "factory";

        return factoryCandidateTypeNames.Count > 1 ? "factory-candidates" : "unknown";
    }

    private static double ComputeResolutionConfidence(
        string? declaredTypeName,
        string? factoryTypeName,
        IReadOnlyList<string> factoryCandidateTypeNames)
    {
        if (!string.IsNullOrWhiteSpace(declaredTypeName) && !string.IsNullOrWhiteSpace(factoryTypeName))
        {
            return string.Equals(declaredTypeName, factoryTypeName, StringComparison.Ordinal)
                ? 1.0
                : 0.4;
        }

        if (!string.IsNullOrWhiteSpace(declaredTypeName) && factoryCandidateTypeNames.Count > 1)
            return factoryCandidateTypeNames.Contains(declaredTypeName, StringComparer.Ordinal) ? 0.9 : 0.6;

        if (!string.IsNullOrWhiteSpace(declaredTypeName))
            return 0.9;

        if (!string.IsNullOrWhiteSpace(factoryTypeName))
            return 0.85;

        return factoryCandidateTypeNames.Count > 1 ? 0.5 : 0.0;
    }
}
