using Microsoft.CodeAnalysis;
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
    internal static string? TryResolveDeclaredTypeName(string sourceText, string identifier, string? sourceFilePath = null)
    {
        var resolved = TryResolveDeclaredTypeNameFromSyntax(sourceText, identifier);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        if (string.IsNullOrWhiteSpace(sourceFilePath))
        {
            return null;
        }

        var projectDir = AssemblyResolver.TryGetProjectDirectory(sourceFilePath);
        if (string.IsNullOrWhiteSpace(projectDir))
        {
            return null;
        }

        var normalizedCurrent = Path.GetFullPath(sourceFilePath);
        foreach (var file in Directory.GetFiles(projectDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(Path.GetFullPath(file), normalizedCurrent, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            resolved = TryResolveDeclaredTypeNameFromSyntax(text, identifier);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    internal static string? TryResolveDbContextTypeName(
        string sourceText,
        string contextVariableName,
        string? sourceFilePath = null)
        => TryResolveDeclaredTypeName(sourceText, contextVariableName, sourceFilePath);

    private static string? TryResolveDeclaredTypeNameFromSyntax(string sourceText, string identifier)
    {
        if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        try
        {
            var root = CSharpSyntaxTree.ParseText(sourceText).GetRoot();
            return ResolveTypeNameForIdentifier(root, identifier);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveTypeNameForIdentifier(SyntaxNode root, string identifier)
    {
        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            var variable = field.Declaration.Variables.FirstOrDefault(v =>
                v.Identifier.ValueText.Equals(identifier, StringComparison.Ordinal));
            if (variable is not null)
            {
                return ResolveTypeFromDeclaration(field.Declaration.Type, variable, root);
            }
        }

        foreach (var local in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            var variable = local.Declaration.Variables.FirstOrDefault(v =>
                v.Identifier.ValueText.Equals(identifier, StringComparison.Ordinal));
            if (variable is not null)
            {
                return ResolveTypeFromDeclaration(local.Declaration.Type, variable, root);
            }
        }

        foreach (var parameter in root.DescendantNodes().OfType<ParameterSyntax>())
        {
            if (parameter.Identifier.ValueText.Equals(identifier, StringComparison.Ordinal)
                && parameter.Type is not null)
            {
                return ResolveTypeFromDeclaration(parameter.Type, variable: null, root);
            }
        }

        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            var parameterList = typeDecl.ParameterList;
            if (parameterList is null)
            {
                continue;
            }

            foreach (var parameter in parameterList.Parameters)
            {
                if (parameter.Identifier.ValueText.Equals(identifier, StringComparison.Ordinal)
                    && parameter.Type is not null)
                {
                    return ResolveTypeFromDeclaration(parameter.Type, variable: null, root);
                }
            }
        }

        return null;
    }

    private static string? ResolveTypeFromDeclaration(
        TypeSyntax typeSyntax,
        VariableDeclaratorSyntax? variable,
        SyntaxNode root)
    {
        if (!IsVarType(typeSyntax))
        {
            return NormalizeDbContextTypeName(typeSyntax.ToString());
        }

        return variable is not null
            ? TryInferDbContextTypeFromVarInitializer(variable, root)
            : null;
    }

    private static bool IsVarType(TypeSyntax typeSyntax)
        => typeSyntax is IdentifierNameSyntax { IsVar: true };

    /// <summary>
    /// <c>var context = await contextFactory.CreateDbContextAsync(ct)</c> — infer the
    /// DbContext type from <c>IDbContextFactory&lt;T&gt;</c> on the factory variable.
    /// </summary>
    private static string? TryInferDbContextTypeFromVarInitializer(
        VariableDeclaratorSyntax variable,
        SyntaxNode root)
    {
        var initializer = variable.Initializer?.Value;
        if (initializer is not AwaitExpressionSyntax awaitExpr)
        {
            return null;
        }

        if (!TryGetCreateDbContextFactoryIdentifier(awaitExpr.Expression, out var factoryIdentifier))
        {
            return null;
        }

        var factoryTypeName = ResolveTypeNameForIdentifier(root, factoryIdentifier);
        return TryExtractFirstGenericTypeArgument(factoryTypeName);
    }

    private static bool TryGetCreateDbContextFactoryIdentifier(
        ExpressionSyntax expression,
        out string factoryIdentifier)
    {
        factoryIdentifier = string.Empty;
        if (expression is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        var methodName = GetInvokedMethodName(invocation);
        if (methodName is not ("CreateDbContextAsync" or "CreateDbContext"))
        {
            return false;
        }

        if (invocation.Expression is MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax factoryId,
            })
        {
            factoryIdentifier = factoryId.Identifier.ValueText;
            return factoryIdentifier.Length > 0;
        }

        return false;
    }

    private static string? TryExtractFirstGenericTypeArgument(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        var open = typeName.IndexOf('<');
        if (open < 0)
        {
            return null;
        }

        var close = typeName.LastIndexOf('>');
        if (close <= open)
        {
            return null;
        }

        var inner = typeName[(open + 1)..close].Trim();
        if (inner.StartsWith("global::", StringComparison.Ordinal))
        {
            inner = inner["global::".Length..];
        }

        return string.IsNullOrWhiteSpace(inner) ? null : NormalizeDbContextTypeName(inner);
    }

    /// <summary>
    /// Normalises a syntactically-resolved type name for use as a DbContext disambiguator.
    /// Strips nullable-reference-type annotations (<c>?</c>) — they have no CLR distinction.
    /// Returns <c>null</c> for <c>var</c> — it is not a real CLR type name.
    /// </summary>
    private static string? NormalizeDbContextTypeName(string typeName)
    {
        var trimmed = typeName.TrimEnd('?');
        return trimmed.Equals("var", StringComparison.Ordinal) ? null : trimmed;
    }
}
