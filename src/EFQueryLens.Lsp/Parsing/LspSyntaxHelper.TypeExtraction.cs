using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using EFQueryLens.Core.Contracts;
using System.Text.RegularExpressions;

namespace EFQueryLens.Lsp.Parsing;

public static partial class LspSyntaxHelper
{
    /// <summary>
    /// Returns syntactically-determined type name strings for local variables visible at
    /// the cursor position. Only yields entries where the type can be determined without
    /// a semantic model — explicit declarations are always included; <c>var</c> declarations
    /// are included only when the type is inferable from the initializer expression.
    /// </summary>
    internal static Dictionary<string, string> ExtractLocalVariableTypesAtPosition(
        string sourceText, int line, int character)
    {
        var hints = ExtractLocalSymbolHintsAtPosition(sourceText, line, character, targetAssemblyPath: null);
        return hints
            .GroupBy(h => h.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().TypeName, StringComparer.Ordinal);
    }

    internal static Dictionary<string, string> ExtractLocalVariableTypesAtPosition(
        string sourceText, int line, int character, string? targetAssemblyPath)
    {
        var hints = ExtractLocalSymbolHintsAtPosition(sourceText, line, character, targetAssemblyPath);
        return hints
            .GroupBy(h => h.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().TypeName, StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns rich symbol hints for variables visible at cursor position.
    /// Includes method/local-function/lambda parameters and local declarations.
    /// </summary>
    internal static IReadOnlyList<LocalSymbolHint> ExtractLocalSymbolHintsAtPosition(
        string sourceText, int line, int character)
        => ExtractLocalSymbolHintsAtPosition(sourceText, line, character, targetAssemblyPath: null);

    internal static IReadOnlyList<LocalSymbolHint> ExtractLocalSymbolHintsAtPosition(
        string sourceText, int line, int character, string? targetAssemblyPath)
    {
        var result = new Dictionary<string, LocalSymbolHint>(StringComparer.Ordinal);
        try
        {
            SyntaxTree tree;
            SyntaxNode root;
            SemanticModel? semanticModel = null;
            if (TryCreateSemanticModel(sourceText, targetAssemblyPath, out tree, out root, out var model))
            {
                semanticModel = model;
            }
            else
            {
                tree = CSharpSyntaxTree.ParseText(sourceText);
                root = tree.GetRoot();
            }

            var textLines = tree.GetText().Lines;

            if (line >= textLines.Count) return [];

            var lineStart = textLines[line].Start;
            var charOffset = Math.Min(character, textLines[line].End - lineStart);
            var cursorPosition = lineStart + charOffset;

            var node = root.FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(cursorPosition, 0));
            var anchorStatement = node.FirstAncestorOrSelf<StatementSyntax>();
            if (anchorStatement is null) return [];

            CollectLocalsInScope(anchorStatement, result, semanticModel);
        }
        catch
        {
            // Best-effort — never propagate to caller.
        }

        return result.Values.ToArray();
    }

    internal static IReadOnlyList<LocalSymbolGraphEntry> ExtractLocalSymbolGraphAtPosition(
        string sourceText,
        int line,
        int character,
        string? targetAssemblyPath)
    {
        var hints = ExtractLocalSymbolHintsAtPosition(sourceText, line, character, targetAssemblyPath);
        return hints
            .GroupBy(h => h.Name, StringComparer.Ordinal)
            .Select(g => g.OrderBy(h => h.DeclarationOrder).First())
            .OrderBy(h => h.DeclarationOrder)
            .ThenBy(h => h.Name, StringComparer.Ordinal)
            .Select(h => new LocalSymbolGraphEntry
            {
                Name = h.Name,
                TypeName = h.TypeName,
                Kind = h.Kind,
                InitializerExpression = h.InitializerExpression,
                DeclarationOrder = h.DeclarationOrder,
                Dependencies = h.Dependencies,
                Scope = h.Scope,
            })
            .ToArray();
    }

    internal static IReadOnlyList<MemberTypeHint> BuildMemberTypeHints(
        string expression,
        IEnumerable<LocalSymbolHint> localSymbolHints)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return [];

        var receiverTypes = localSymbolHints
            .Where(h => !string.IsNullOrWhiteSpace(h.Name) && !string.IsNullOrWhiteSpace(h.TypeName))
            .GroupBy(h => h.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().TypeName, StringComparer.Ordinal);

        if (receiverTypes.Count == 0)
            return [];

        var map = new Dictionary<(string Receiver, string Member), string>();
        foreach (var kv in receiverTypes)
        {
            if (!TryGetNullableUnderlyingTypeName(kv.Value, out var underlying))
                continue;

            map[(kv.Key, "HasValue")] = "bool";
            map[(kv.Key, "Value")] = underlying!;
        }

        if (map.Count == 0)
            return [];

        var hints = new List<MemberTypeHint>();
        foreach (var ((receiver, member), typeName) in map
                     .OrderBy(x => x.Key.Receiver, StringComparer.Ordinal)
                     .ThenBy(x => x.Key.Member, StringComparer.Ordinal))
        {
            if (!expression.Contains($"{receiver}.{member}", StringComparison.Ordinal))
                continue;

            hints.Add(new MemberTypeHint
            {
                ReceiverName = receiver,
                MemberName = member,
                TypeName = typeName,
            });
        }

        return hints;
    }

    private static bool TryGetNullableUnderlyingTypeName(string typeName, out string? underlying)
    {
        underlying = null;
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        var trimmed = typeName.Trim();
        if (trimmed.EndsWith("?", StringComparison.Ordinal) && trimmed.Length > 1)
        {
            underlying = trimmed[..^1];
            return true;
        }

        const string nullablePrefix = "System.Nullable<";
        if (trimmed.StartsWith(nullablePrefix, StringComparison.Ordinal)
            && trimmed.EndsWith(">", StringComparison.Ordinal)
            && trimmed.Length > nullablePrefix.Length + 1)
        {
            underlying = trimmed[nullablePrefix.Length..^1];
            return true;
        }

        if (trimmed.StartsWith("Nullable<", StringComparison.Ordinal)
            && trimmed.EndsWith(">", StringComparison.Ordinal)
            && trimmed.Length > "Nullable<".Length + 1)
        {
            underlying = trimmed["Nullable<".Length..^1];
            return true;
        }

        return false;
    }

    private static void CollectLocalsInScope(
        StatementSyntax anchorStatement,
        Dictionary<string, LocalSymbolHint> result,
        SemanticModel? semanticModel)
    {
        var declarationOrder = 0;
        var visited = anchorStatement;
        var scopeId = anchorStatement.FirstAncestorOrSelf<MemberDeclarationSyntax>()?.ToString();
        var scopeChain = new List<SyntaxNode>();

        for (SyntaxNode? scope = anchorStatement.Parent; scope is not null; scope = scope.Parent)
        {
            scopeChain.Add(scope);
        }

        // Collect parameter symbols first (outer-to-inner) so dependent locals
        // (for example page/pageSize initialized from request.*) always sort after
        // the parameters they depend on.
        for (var i = scopeChain.Count - 1; i >= 0; i--)
        {
            CollectParametersFromScope(scopeChain[i], result, ref declarationOrder, scopeId);
        }

        for (var scopeIndex = 0; scopeIndex < scopeChain.Count; scopeIndex++)
        {
            var scope = scopeChain[scopeIndex];
            if (!TryGetStatementContainer(scope, out var statements))
                continue;

            var anchorIndex = statements.FindIndex(s => ReferenceEquals(s, visited));
            if (anchorIndex >= 0)
            {
                for (var i = 0; i < anchorIndex; i++)
                {
                    if (statements[i] is not LocalDeclarationStatementSyntax localDecl)
                        continue;

                    foreach (var variable in localDecl.Declaration.Variables)
                    {
                        var varName = variable.Identifier.ValueText;
                        var typeName = GetTypeStringForDeclaration(localDecl.Declaration.Type, variable, semanticModel);
                        if (typeName is null)
                        {
                            continue;
                        }

                        var initializerExpression = variable.Initializer?.Value is { } init
                            ? NormalizeInitializerExpression(init)
                            : null;

                        result[varName] = new LocalSymbolHint
                        {
                            Name = varName,
                            TypeName = typeName,
                            Kind = "local",
                            InitializerExpression = initializerExpression,
                            DeclarationOrder = declarationOrder++,
                            Dependencies = ExtractInitializerDependencies(variable, semanticModel),
                            Scope = scopeId,
                        };
                    }
                }
            }

            // Move anchor up to the statement containing this scope node.
            var outerStatement = scope.Parent?.FirstAncestorOrSelf<StatementSyntax>();
            if (outerStatement is not null)
                visited = outerStatement;
        }
    }

    private static void CollectParametersFromScope(
        SyntaxNode scope,
        Dictionary<string, LocalSymbolHint> result,
        ref int declarationOrder,
        string? scopeId)
    {
        ParameterListSyntax? paramList = scope switch
        {
            MethodDeclarationSyntax m => m.ParameterList,
            ConstructorDeclarationSyntax c => c.ParameterList,
            LocalFunctionStatementSyntax lf => lf.ParameterList,
            ParenthesizedLambdaExpressionSyntax l => l.ParameterList,
            _ => null,
        };

        if (scope is SimpleLambdaExpressionSyntax simpleLambda)
        {
            var param = simpleLambda.Parameter;
            var paramName = param.Identifier.ValueText;
            var typeName = param.Type?.ToString();
            if (!string.IsNullOrWhiteSpace(paramName)
                && !result.ContainsKey(paramName)
                && !string.IsNullOrWhiteSpace(typeName))
            {
                result[paramName] = new LocalSymbolHint
                {
                    Name = paramName,
                    TypeName = typeName!,
                    Kind = "lambda-parameter",
                    DeclarationOrder = declarationOrder++,
                    Scope = scopeId,
                };
            }
        }

        if (paramList is null) return;

        // Collect open generic type parameter names so we can normalize parameter types that
        // reference them. For example, in GetResults<TResult>(...,
        // Expression<Func<Entity, TResult>> selector), "TResult" cannot be resolved in the
        // daemon eval script. We replace open generic parameter names with "object" so Core
        // can still build a deterministic typed stub without semantic-model binding.
        var openTypeParams = scope switch
        {
            MethodDeclarationSyntax { TypeParameterList: { } tpl } =>
                tpl.Parameters.Select(p => p.Identifier.ValueText).ToHashSet(StringComparer.Ordinal),
            LocalFunctionStatementSyntax { TypeParameterList: { } tpl } =>
                tpl.Parameters.Select(p => p.Identifier.ValueText).ToHashSet(StringComparer.Ordinal),
            _ => null,
        };

        foreach (var param in paramList.Parameters)
        {
            var paramName = param.Identifier.ValueText;
            if (result.ContainsKey(paramName) || param.Type is null) continue;

            var typeName = param.Type.ToString();
            if (string.IsNullOrWhiteSpace(typeName)) continue;

            if (openTypeParams is not null && openTypeParams.Count > 0)
                typeName = ReplaceOpenGenericTypeParameters(typeName, openTypeParams);

            result[paramName] = new LocalSymbolHint
            {
                Name = paramName,
                TypeName = typeName,
                Kind = scope is ParenthesizedLambdaExpressionSyntax ? "lambda-parameter" : "parameter",
                DeclarationOrder = declarationOrder++,
                Scope = scopeId,
            };
        }
    }

    private static string? GetTypeStringForDeclaration(TypeSyntax typeSyntax, VariableDeclaratorSyntax variable)
        => GetTypeStringForDeclaration(typeSyntax, variable, semanticModel: null);

    private static string? GetTypeStringForDeclaration(
        TypeSyntax typeSyntax,
        VariableDeclaratorSyntax variable,
        SemanticModel? semanticModel)
    {
        // Explicit type declaration: int x = ..., Guid id = ..., List<string> names = ...
        if (typeSyntax is not IdentifierNameSyntax { IsVar: true })
            return typeSyntax.ToString();

        // var x = ... — try to infer from initializer expression.
        if (variable.Initializer?.Value is { } initializer)
        {
            var syntaxType = TryInferTypeFromInitializer(initializer);
            if (!string.IsNullOrWhiteSpace(syntaxType))
                return syntaxType;
        }

        return TryInferVarTypeFromSemanticModel(variable, semanticModel);
    }

    private static string? TryInferVarTypeFromSemanticModel(
        VariableDeclaratorSyntax variable,
        SemanticModel? semanticModel)
    {
        if (semanticModel is null)
            return null;

        var symbol = semanticModel.GetDeclaredSymbol(variable) as ILocalSymbol;
        var type = symbol?.Type;
        if (type is null || type.TypeKind == TypeKind.Error)
            return null;

        return type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    private static string? TryInferTypeFromInitializer(ExpressionSyntax initializer) =>
        initializer switch
        {
            // new TypeName(...) or new TypeName<T>(...)
            ObjectCreationExpressionSyntax creation => creation.Type.ToString(),

            // (TypeName)expr
            CastExpressionSyntax cast => cast.Type.ToString(),

            // default(TypeName)
            DefaultExpressionSyntax { Type: { } t } => t.ToString(),

            // new TypeName[] { ... }
            ArrayCreationExpressionSyntax array => array.Type.ToString(),

            // new[] { ... } — element type unknown without semantic model
            ImplicitArrayCreationExpressionSyntax => null,

            // new() — target type unknown without semantic model
            ImplicitObjectCreationExpressionSyntax => null,
            
            // condition ? trueExpr : falseExpr — infer from true branch (should match false)
            ConditionalExpressionSyntax conditional 
                => TryInferTypeFromInitializer(conditional.WhenTrue) 
                   ?? TryInferTypeFromInitializer(conditional.WhenFalse),

            // 5, 5L, 5.0f, "str", true, false, ...
            LiteralExpressionSyntax literal => literal.Kind() switch
            {
                SyntaxKind.NumericLiteralExpression => InferNumericLiteralType(literal.Token),
                SyntaxKind.StringLiteralExpression => "string",
                SyntaxKind.CharacterLiteralExpression => "char",
                SyntaxKind.TrueLiteralExpression => "bool",
                SyntaxKind.FalseLiteralExpression => "bool",
                _ => null,
            },

            // $"..." is always System.String
            InterpolatedStringExpressionSyntax => "string",

            _ => null,
        };

    private static string? InferNumericLiteralType(SyntaxToken token)
    {
        var text = token.Text;
        if (text.EndsWith("L", StringComparison.OrdinalIgnoreCase)) return "long";
        if (text.EndsWith("m", StringComparison.OrdinalIgnoreCase)) return "decimal";
        if (text.EndsWith("f", StringComparison.OrdinalIgnoreCase)) return "float";
        if (text.EndsWith("d", StringComparison.OrdinalIgnoreCase)) return "double";
        if (text.Contains('.')) return "double";
        return "int";
    }

    private static string ReplaceOpenGenericTypeParameters(string typeName, IReadOnlyCollection<string> openTypeParams)
    {
        var rewritten = typeName;
        foreach (var typeParam in openTypeParams)
        {
            if (string.IsNullOrWhiteSpace(typeParam))
                continue;

            rewritten = Regex.Replace(
                rewritten,
                $@"\b{Regex.Escape(typeParam)}\b",
                "object",
                RegexOptions.CultureInvariant);
        }

        return rewritten;
    }

    private static string NormalizeInitializerExpression(ExpressionSyntax initializer) =>
        initializer.WithoutTrivia().NormalizeWhitespace().ToString();

    private static IReadOnlyList<string> ExtractInitializerDependencies(
        VariableDeclaratorSyntax variable,
        SemanticModel? semanticModel)
    {
        if (semanticModel is null || variable.Initializer?.Value is not { } init)
            return [];

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in init.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            var symbol = semanticModel.GetSymbolInfo(id).Symbol;
            if (symbol is ILocalSymbol or IParameterSymbol)
            {
                var name = id.Identifier.ValueText;
                if (!string.Equals(name, variable.Identifier.ValueText, StringComparison.Ordinal))
                    names.Add(name);
            }
        }

        return names.OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }

}
