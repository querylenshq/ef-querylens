using System.Reflection;
using EFQueryLens.Core.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Core.Scripting.Evaluation;

internal sealed partial class CompilationPipeline
{
    private static string? TryExtractMissingIdentifierFromDiagnostic(Diagnostic diagnostic)
    {
        if (!diagnostic.Location.IsInSource || diagnostic.Location.SourceTree is null)
            return null;

        var root = diagnostic.Location.SourceTree.GetRoot();
        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

        var identifier = node as IdentifierNameSyntax
            ?? node.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().FirstOrDefault()
            ?? node.AncestorsAndSelf().OfType<IdentifierNameSyntax>().FirstOrDefault();

        if (identifier is not null)
            return identifier.Identifier.ValueText;

        var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
        return token.IsKind(SyntaxKind.IdentifierToken)
            ? token.ValueText
            : null;
    }

    private bool ApplyCompileRetryAdjustments(
        IReadOnlyList<Diagnostic> errors,
        CSharpCompilation compilation,
        IReadOnlyList<Assembly> compilationAssemblies,
        IReadOnlySet<string> knownNamespaces,
        IReadOnlySet<string> knownTypes,
        Type dbContextType,
        TranslationRequest workingRequest,
        List<string> stubs,
        ISet<string> synthesizedUsingStaticTypes,
        ISet<string> synthesizedUsingNamespaces,
        ref string workingExpression,
        ref bool includeGridifyFallbackExtensions)
    {
        var changed = false;

        changed |= ApplyMissingIdentifierStubRule(errors, workingRequest, dbContextType, stubs);
        changed |= ApplyMissingTypeImportRule(errors, knownNamespaces, knownTypes, synthesizedUsingStaticTypes, synthesizedUsingNamespaces);
        changed |= ApplyArgumentTypeRestubRule(errors, dbContextType, workingRequest, stubs);
        changed |= ApplyExpressionNormalizationRules(errors, compilation, workingRequest.Expression, dbContextType, ref workingExpression);
        changed |= ApplyMissingExtensionImportRule(errors, compilation, compilationAssemblies, synthesizedUsingStaticTypes);

        if (StubSynthesizer.TryApplyGridifyFallbackFromErrors(errors, stubs, ref includeGridifyFallbackExtensions))
        {
            changed = true;
        }

        return changed;
    }

    private bool ApplyMissingIdentifierStubRule(
        IReadOnlyList<Diagnostic> errors,
        TranslationRequest workingRequest,
        Type dbContextType,
        ICollection<string> stubs)
    {
        var changed = false;
        var missingNames = errors
            .Where(d => d.Id == "CS0103")
            .Select(TryExtractMissingIdentifierFromDiagnostic)
            .Where(n => n is not null)
            .Distinct()
            .Where(n => !string.IsNullOrWhiteSpace(n)
                        && !StubSynthesizer.LooksLikeTypeOrNamespacePrefix(n, workingRequest.Expression, workingRequest.UsingAliases))
            .ToList();

        LogDebug($"compile-retry cs0103-missing-names={string.Join(",", missingNames)}");

        var rootId = ImportResolver.TryExtractRootIdentifier(workingRequest.Expression);
        foreach (var missingName in missingNames)
        {
            if (stubs.Any(s => s.Contains($" {missingName} ", StringComparison.Ordinal) || s.Contains($" {missingName};", StringComparison.Ordinal)))
                continue;

            var stub = StubSynthesizer.BuildStubDeclaration(missingName!, rootId, workingRequest, dbContextType);
            if (string.IsNullOrWhiteSpace(stub))
                continue;

            LogDebug($"compile-retry stub-added name={missingName} stub={stub.Trim()}");
            stubs.Add(stub);
            changed = true;
        }

        return changed;
    }

    private bool ApplyMissingTypeImportRule(
        IReadOnlyList<Diagnostic> errors,
        IReadOnlySet<string> knownNamespaces,
        IReadOnlySet<string> knownTypes,
        ISet<string> synthesizedUsingStaticTypes,
        ISet<string> synthesizedUsingNamespaces)
    {
        // CS0246 / CS0234: type or namespace not found. The type likely lives in a
        // namespace that the source file doesn't need to import explicitly (e.g. a DTO
        // in the same namespace as the calling class). Locate it in the loaded
        // assemblies and synthesise a using directive automatically.
        // Use the diagnostic message text (same regex as FormatSoftDiagnostics) to
        // extract the simple type name - more reliable than AST span lookup.
        //
        // Two cases:
        //  * Top-level type   -> parent is a namespace -> emit "using Ns;"
        //  * Nested type      -> parent is a class     -> emit "using static EnclosingType;"
        var changed = false;
        var missingTypes = errors
            .Where(d => d.Id is "CS0246" or "CS0234")
            .Select(QueryEvaluator.TryExtractTypeNameFromCS0246)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        LogDebug($"compile-retry cs0246-types={string.Join(",", missingTypes)}");

        foreach (var typeName in missingTypes)
        {
            var parents = ImportResolver.FindNamespacesForSimpleName(typeName!, knownTypes).ToList();
            LogDebug($"compile-retry type-lookup name={typeName} found-parents={string.Join(",", parents)}");

            if (parents.Count == 0)
            {
                LogDebug($"compile-retry type-not-in-known-types name={typeName}");
            }

            foreach (var parent in parents)
            {
                if (ImportResolver.IsResolvableNamespace(parent, knownNamespaces))
                {
                    if (synthesizedUsingNamespaces.Add(parent))
                    {
                        LogDebug($"compile-retry using-namespace added={parent}");
                        changed = true;
                    }
                }
                else if (ImportResolver.IsResolvableType(parent, knownTypes))
                {
                    if (synthesizedUsingStaticTypes.Add(parent))
                    {
                        LogDebug($"compile-retry using-static added={parent}");
                        changed = true;
                    }
                }
            }
        }

        return changed;
    }

    private static bool ApplyArgumentTypeRestubRule(
        IReadOnlyList<Diagnostic> errors,
        Type dbContextType,
        TranslationRequest workingRequest,
        ICollection<string> stubs)
    {
        // CS1503: argument type mismatch - a stub was generated as 'object' because
        // no type could be inferred (e.g. a pattern string passed to EF.Functions.Like).
        // Extract the expected type from the diagnostic message and re-generate the
        // stub with the correct type so overload resolution succeeds on retry.
        var changed = false;

        foreach (var cs1503 in errors.Where(e => e.Id == "CS1503"))
        {
            var argName = TryExtractSimpleIdentifierAtDiagnosticLocation(cs1503);
            if (argName is null)
                continue;

            var expectedType = QueryEvaluator.TryExtractExpectedTypeFromCS1503(cs1503);
            if (string.IsNullOrWhiteSpace(expectedType))
                continue;

            var oldStub = stubs.FirstOrDefault(s =>
                s.Contains($" {argName} ", StringComparison.Ordinal)
                || s.Contains($" {argName};", StringComparison.Ordinal));
            if (oldStub is null)
                continue;

            var typedStub = StubSynthesizer.BuildStubFromTypeName(expectedType, argName, dbContextType, workingRequest.UsingAliases);
            if (string.IsNullOrWhiteSpace(typedStub))
            {
                stubs.Remove(oldStub);
                changed = true;
                continue;
            }

            if (string.Equals(oldStub, typedStub, StringComparison.Ordinal))
                continue;

            stubs.Remove(oldStub);
            stubs.Add(typedStub);
            changed = true;
        }

        return changed;
    }

    private static bool ApplyExpressionNormalizationRules(
        IReadOnlyList<Diagnostic> errors,
        CSharpCompilation compilation,
        string originalExpression,
        Type dbContextType,
        ref string workingExpression)
    {
        var changed = false;

        if (ImportResolver.TryNormalizeRootContextHopFromErrors(
                errors,
                compilation,
                originalExpression,
                dbContextType,
                out var normalizedExpression)
            && !string.Equals(normalizedExpression, workingExpression, StringComparison.Ordinal))
        {
            workingExpression = normalizedExpression;
            changed = true;
        }

        if (ImportResolver.TryNormalizePatternTernaryComparisonFromErrors(
                errors,
                workingExpression,
                out var ternaryNormalizedExpression)
            && !string.Equals(ternaryNormalizedExpression, workingExpression, StringComparison.Ordinal))
        {
            workingExpression = ternaryNormalizedExpression;
            changed = true;
        }

        if (ImportResolver.TryNormalizeUnsupportedPatternMatchingFromErrors(
                errors,
                workingExpression,
                out var patternNormalizedExpression)
            && !string.Equals(patternNormalizedExpression, workingExpression, StringComparison.Ordinal))
        {
            workingExpression = patternNormalizedExpression;
            changed = true;
        }

        if (TryNormalizeInaccessibleProjectionTypeFromErrors(
                errors,
                workingExpression,
                out var inaccessibleProjectionNormalizedExpression)
            && !string.Equals(inaccessibleProjectionNormalizedExpression, workingExpression, StringComparison.Ordinal))
        {
            workingExpression = inaccessibleProjectionNormalizedExpression;
            changed = true;
        }

        return changed;
    }

    private static bool ApplyMissingExtensionImportRule(
        IReadOnlyList<Diagnostic> errors,
        CSharpCompilation compilation,
        IReadOnlyList<Assembly> compilationAssemblies,
        ISet<string> synthesizedUsingStaticTypes)
    {
        var changed = false;
        foreach (var import in ImportResolver.InferMissingExtensionStaticImports(errors, compilation, compilationAssemblies))
        {
            if (synthesizedUsingStaticTypes.Add(import))
            {
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Returns the identifier name at the diagnostic location only when the AST node
    /// is a bare <see cref="IdentifierNameSyntax"/> (i.e. a simple variable reference,
    /// not a member access or method invocation). Used for CS1503 re-stub logic: a
    /// compound expression like <c>someObj.Pattern</c> at the error site would require
    /// changing the stub for <c>someObj</c>, which could be wrong if it is used with a
    /// different type elsewhere in the expression.
    /// </summary>
    private static string? TryExtractSimpleIdentifierAtDiagnosticLocation(Diagnostic diagnostic)
    {
        if (!diagnostic.Location.IsInSource || diagnostic.Location.SourceTree is null)
            return null;

        var root = diagnostic.Location.SourceTree.GetRoot();
        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

        return node is IdentifierNameSyntax identifier
            ? identifier.Identifier.ValueText
            : null;
    }

}