using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using EFQueryLens.Core.Contracts;

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
        var hints = ExtractLocalSymbolHintsAtPosition(sourceText, line, character);
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
    {
        var result = new Dictionary<string, LocalSymbolHint>(StringComparer.Ordinal);
        try
        {
            var tree = CSharpSyntaxTree.ParseText(sourceText);
            var root = tree.GetRoot();
            var textLines = tree.GetText().Lines;

            if (line >= textLines.Count) return [];

            var lineStart = textLines[line].Start;
            var charOffset = Math.Min(character, textLines[line].End - lineStart);
            var cursorPosition = lineStart + charOffset;

            var node = root.FindNode(new Microsoft.CodeAnalysis.Text.TextSpan(cursorPosition, 0));
            var anchorStatement = node.FirstAncestorOrSelf<StatementSyntax>();
            if (anchorStatement is null) return [];

            CollectLocalsInScope(anchorStatement, result);
        }
        catch
        {
            // Best-effort — never propagate to caller.
        }

        return result.Values.ToArray();
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

    private static void CollectLocalsInScope(StatementSyntax anchorStatement, Dictionary<string, LocalSymbolHint> result)
    {
        var visited = anchorStatement;

        for (SyntaxNode? scope = anchorStatement.Parent; scope is not null; scope = scope.Parent)
        {
            // Collect parameters from any enclosing method / constructor / local function.
            CollectParametersFromScope(scope, result);

            if (!TryGetStatementContainer(scope, out var statements))
                continue;

            var anchorIndex = statements.FindIndex(s => ReferenceEquals(s, visited));
            if (anchorIndex >= 0)
            {
                for (var i = anchorIndex - 1; i >= 0; i--)
                {
                    if (statements[i] is not LocalDeclarationStatementSyntax localDecl)
                        continue;

                    foreach (var variable in localDecl.Declaration.Variables)
                    {
                        var varName = variable.Identifier.ValueText;
                        if (result.ContainsKey(varName)) continue;

                        var typeName = GetTypeStringForDeclaration(localDecl.Declaration.Type, variable);
                        if (typeName is not null)
                        {
                            result[varName] = new LocalSymbolHint
                            {
                                Name = varName,
                                TypeName = typeName,
                                Kind = "local",
                            };
                        }
                    }
                }
            }

            // Move anchor up to the statement containing this scope node.
            var outerStatement = scope.Parent?.FirstAncestorOrSelf<StatementSyntax>();
            if (outerStatement is not null)
                visited = outerStatement;
        }
    }

    private static void CollectParametersFromScope(SyntaxNode scope, Dictionary<string, LocalSymbolHint> result)
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
                };
            }
        }

        if (paramList is null) return;

        // Collect open generic type parameter names so we can skip method parameters whose
        // declared types reference them. For example, in GetResults<TResult>(...,
        // Expression<Func<Entity, TResult>> selector), the string "TResult" cannot be
        // resolved inside the eval script and causes a compile error. Excluding the parameter
        // from LocalVariableTypes lets the existing Expression<Func<...>> heuristic in
        // BuildStubDeclaration generate a correct "_ => default!" stub instead.
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

            // Skip parameters whose declared type contains an open generic type parameter name.
            if (openTypeParams is not null &&
                openTypeParams.Any(tp => typeName.Contains(tp, StringComparison.Ordinal)))
            {
                // Log: parameter skipped due to open generic type reference
                System.Diagnostics.Debug.WriteLine(
                    $"[LspSyntaxHelper] skip-param name={paramName} type={typeName} contains-open-generic");
                continue;
            }

            result[paramName] = new LocalSymbolHint
            {
                Name = paramName,
                TypeName = typeName,
                Kind = scope is ParenthesizedLambdaExpressionSyntax ? "lambda-parameter" : "parameter",
            };
        }
    }

    private static string? GetTypeStringForDeclaration(TypeSyntax typeSyntax, VariableDeclaratorSyntax variable)
    {
        // Explicit type declaration: int x = ..., Guid id = ..., List<string> names = ...
        if (typeSyntax is not IdentifierNameSyntax { IsVar: true })
            return typeSyntax.ToString();

        // var x = ... — try to infer from initializer expression.
        return variable.Initializer?.Value is { } initializer
            ? TryInferTypeFromInitializer(initializer)
            : null;
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

            // TypeName.Property — heuristic: PascalCase receiver is likely a type.
            // Skip known static utility classes (e.g. Math.PI) for the same reason as
            // the InvocationExpressionSyntax arm below: emitting "Math" as a type name
            // causes the evaluator to block stub generation for the variable entirely.
            MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver }
                when IsProbablyTypeName(receiver.Identifier.ValueText)
                  && !IsKnownStaticUtilityClass(receiver.Identifier.ValueText)
                => receiver.Identifier.ValueText,

            // TypeName.Method(...)
            // Only emit a type name when the receiver looks like a concrete instance type,
            // not a static BCL utility class (Math, Convert, Enum, etc.). Static types
            // cannot be instantiated as stubs, so returning their name here causes the
            // evaluator to skip the variable entirely and then fail with an unknown-variable
            // error. Returning null lets numeric/heuristic synthesis handle paging variables
            // like `var page = Math.Max(...)` or `var clamped = Math.Clamp(...)`.
            InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver2 }
            } when IsProbablyTypeName(receiver2.Identifier.ValueText)
                  && !IsKnownStaticUtilityClass(receiver2.Identifier.ValueText)
                => receiver2.Identifier.ValueText,

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

    private static bool IsProbablyTypeName(string identifier) =>
        !string.IsNullOrEmpty(identifier) && char.IsUpper(identifier[0]);

    // Well-known BCL static utility classes whose methods return primitives or unrelated
    // types. When an initializer like `var page = Math.Max(...)` is encountered, inferring
    // the type as "Math" would cause the evaluator to skip stub generation for the variable.
    // Returning null here lets numeric/heuristic synthesis produce a correct int/decimal stub.
    private static readonly HashSet<string> _knownStaticUtilityClasses = new(StringComparer.Ordinal)
    {
        "Math", "Convert", "Enum", "BitConverter", "Buffer",
        "GC", "GCSettings", "Environment", "Console",
        "Path", "File", "Directory", "Interlocked",
        "Monitor", "Mutex", "Semaphore", "Thread",
        "Activator", "Marshal", "RuntimeHelpers",
        "Regex", "Encoding", "Uri",
    };

    private static bool IsKnownStaticUtilityClass(string identifier) =>
        _knownStaticUtilityClasses.Contains(identifier);
}
