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
        ref string workingExpression)
    {
        var changed = false;

        changed |= ApplyMissingTypeImportRule(errors, knownNamespaces, knownTypes, synthesizedUsingStaticTypes, synthesizedUsingNamespaces);
        changed |= ApplyArgumentTypeRestubRule(errors, dbContextType, workingRequest, stubs);
        // LSP-authoritative mode only: daemon expression rewrites are disabled.
        // Keep only CS0122 inaccessible projection normalization, which is required
        // as a generated-assembly visibility bridge.
        changed |= ApplyInaccessibleProjectionNormalizationRule(errors, ref workingExpression);

        changed |= ApplyMissingExtensionImportRule(errors, compilation, compilationAssemblies, synthesizedUsingStaticTypes);
        changed |= ReorderStubsByDependency(stubs);

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

    private bool ApplyInaccessibleProjectionNormalizationRule(
        IReadOnlyList<Diagnostic> errors,
        ref string workingExpression)
    {
        if (TryNormalizeInaccessibleProjectionTypeFromErrors(
                errors,
                workingExpression,
                out var inaccessibleProjectionNormalizedExpression)
            && !string.Equals(inaccessibleProjectionNormalizedExpression, workingExpression, StringComparison.Ordinal))
        {
            LogDebug($"compile-retry expression-mutation rule=inaccessible-projection trigger=CS0122 before-len={workingExpression.Length} after-len={inaccessibleProjectionNormalizedExpression.Length}");
            workingExpression = inaccessibleProjectionNormalizedExpression;
            return true;
        }

        return false;
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

    private static bool ReorderStubsByDependency(List<string> stubs)
    {
        if (stubs.Count <= 1)
            return false;

        var nodes = stubs
            .Select((stub, index) => ParseStubNode(stub, index))
            .ToList();

        var declared = nodes
            .Where(n => !string.IsNullOrWhiteSpace(n.Name))
            .Select(n => n.Name!)
            .ToHashSet(StringComparer.Ordinal);

        if (declared.Count <= 1)
            return false;

        foreach (var node in nodes)
        {
            if (node.Dependencies.Count == 0)
                continue;

            node.Dependencies.RemoveWhere(d =>
                !declared.Contains(d) || string.Equals(d, node.Name, StringComparison.Ordinal));
        }

        var remaining = new List<StubNode>(nodes);
        var ordered = new List<StubNode>(nodes.Count);
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        while (remaining.Count > 0)
        {
            var progressed = false;

            for (var i = 0; i < remaining.Count; i++)
            {
                var node = remaining[i];
                if (!node.Dependencies.All(emitted.Contains))
                    continue;

                ordered.Add(node);
                if (!string.IsNullOrWhiteSpace(node.Name))
                    emitted.Add(node.Name!);
                remaining.RemoveAt(i);
                i--;
                progressed = true;
            }

            if (progressed)
                continue;

            // Cycle or unresolved dependency chain among stubs: preserve original ordering for the rest.
            ordered.AddRange(remaining);
            break;
        }

        var reordered = ordered.Select(n => n.Text).ToList();
        if (reordered.SequenceEqual(stubs, StringComparer.Ordinal))
            return false;

        stubs.Clear();
        stubs.AddRange(reordered);
        return true;
    }

    private static StubNode ParseStubNode(string stub, int index)
    {
        var normalized = stub.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return new StubNode(index, normalized, null, []);

        var statement = SyntaxFactory.ParseStatement(normalized.TrimEnd(';') + ";");
        if (statement is not LocalDeclarationStatementSyntax localDecl
            || localDecl.Declaration.Variables.Count != 1)
        {
            return new StubNode(index, normalized, null, []);
        }

        var variable = localDecl.Declaration.Variables[0];
        var name = variable.Identifier.ValueText;
        var deps = variable.Initializer?.Value is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : variable.Initializer.Value
                .DescendantNodesAndSelf()
                .OfType<IdentifierNameSyntax>()
                .Select(id => id.Identifier.ValueText)
                .Where(id => !string.Equals(id, name, StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);

        return new StubNode(index, normalized, name, deps);
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

    private sealed record StubNode(
        int Index,
        string Text,
        string? Name,
        HashSet<string> Dependencies);

}
