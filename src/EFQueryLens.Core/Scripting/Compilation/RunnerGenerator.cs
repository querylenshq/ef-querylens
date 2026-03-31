using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Core.Scripting.Compilation;

/// <summary>
/// Generates the <c>__QueryLensRunner__</c> class using Roslyn syntax node construction,
/// replacing string-template token substitution for the execution runner.
/// <para>
/// The user expression is parsed as an <see cref="ExpressionSyntax"/> node and embedded
/// directly into the AST, which enables pre-compilation syntax validation and eliminates
/// the risk of token substitution breaking the surrounding source structure.
/// </para>
/// </summary>
internal static class RunnerGenerator
{
    private static readonly CSharpParseOptions SParseOptions =
        new(LanguageVersion.Latest, DocumentationMode.None);

    // ─── Validation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Pre-validates that <paramref name="expression"/> is syntactically valid C#.
    /// Returns an empty list when the expression is valid; otherwise returns
    /// human-readable Roslyn error messages.
    /// </summary>
    internal static IReadOnlyList<string> ValidateExpressionSyntax(string expression)
    {
        var node = SyntaxFactory.ParseExpression(expression, options: SParseOptions);
        return node
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToList();
    }

    // ─── Generation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Generates the complete <c>__QueryLensRunner__</c> class source text.
    /// The <paramref name="expression"/> is parsed into an <see cref="ExpressionSyntax"/>
    /// node and embedded as-is rather than substituted as a raw token.
    /// </summary>
    internal static string GenerateRunnerClass(
        string contextVarName,
        string contextTypeFullName,
        string expression,
        IReadOnlyList<string> stubs,
        bool useAsync)
    {
        var expressionNode = SyntaxFactory.ParseExpression(expression, options: SParseOptions);
        var classDecl = BuildClassDecl(
            contextVarName, contextTypeFullName, expressionNode, stubs, useAsync);
        return classDecl.NormalizeWhitespace().ToFullString() + Environment.NewLine;
    }

    // ─── Class ────────────────────────────────────────────────────────────────

    private static ClassDeclarationSyntax BuildClassDecl(
        string contextVarName,
        string contextTypeFullName,
        ExpressionSyntax expressionNode,
        IReadOnlyList<string> stubs,
        bool useAsync)
    {
        var runMethod = BuildRunMethod(
            contextVarName, contextTypeFullName, expressionNode, stubs, useAsync);
        var helpers = useAsync ? ParseMethods(AsyncHelpersSource) : ParseMethods(SyncHelpersSource);
        var allMembers = new[] { runMethod }.Concat(helpers).ToList();

        var classDecl = SyntaxFactory.ClassDeclaration("__QueryLensRunner__");
        classDecl = AddPublicStaticModifiers(classDecl);
        classDecl = classDecl.WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(allMembers));
        return classDecl;
    }

    /// <summary>
    /// Adds public static modifiers to a class declaration.
    /// </summary>
    private static ClassDeclarationSyntax AddPublicStaticModifiers(ClassDeclarationSyntax classDecl)
    {
        return classDecl.WithModifiers(SyntaxFactory.TokenList(
            SyntaxFactory.Token(SyntaxKind.PublicKeyword),
            SyntaxFactory.Token(SyntaxKind.StaticKeyword)));
    }

    /// <summary>
    /// Adds public static modifiers to a method declaration.
    /// </summary>
    private static MethodDeclarationSyntax AddPublicStaticModifiers(MethodDeclarationSyntax method)
    {
        return method.WithModifiers(SyntaxFactory.TokenList(
            SyntaxFactory.Token(SyntaxKind.PublicKeyword),
            SyntaxFactory.Token(SyntaxKind.StaticKeyword)));
    }

    /// <summary>
    /// Adds public static async modifiers to a method declaration.
    /// </summary>
    private static MethodDeclarationSyntax AddPublicStaticAsyncModifiers(MethodDeclarationSyntax method)
    {
        return method.WithModifiers(SyntaxFactory.TokenList(
            SyntaxFactory.Token(SyntaxKind.PublicKeyword),
            SyntaxFactory.Token(SyntaxKind.StaticKeyword),
            SyntaxFactory.Token(SyntaxKind.AsyncKeyword)));
    }

    // ─── Main run method ─────────────────────────────────────────────────────

    private static MethodDeclarationSyntax BuildRunMethod(
        string contextVarName,
        string contextTypeFullName,
        ExpressionSyntax expressionNode,
        IReadOnlyList<string> stubs,
        bool useAsync)
    {
        var body = BuildRunBody(
            contextVarName, contextTypeFullName, expressionNode, stubs, useAsync);

        if (useAsync)
            return BuildAsyncRunMethod(body);

        return BuildSyncRunMethod(body);
    }

    /// <summary>
    /// Builds the async Run method: public static async Task&lt;object?&gt; RunAsync(object __ctx__, CancellationToken ct = default)
    /// </summary>
    private static MethodDeclarationSyntax BuildAsyncRunMethod(BlockSyntax body)
    {
        var method = SyntaxFactory
            .MethodDeclaration(
                SyntaxFactory.ParseTypeName("System.Threading.Tasks.Task<object?>"),
                SyntaxFactory.Identifier("RunAsync"))
            .WithParameterList(
                SyntaxFactory.ParseParameterList(
                    "(object __ctx__, System.Threading.CancellationToken ct = default)"))
            .WithBody(body);

        return AddPublicStaticAsyncModifiers(method);
    }

    /// <summary>
    /// Builds the sync Run method: public static object? Run(object __ctx__)
    /// </summary>
    private static MethodDeclarationSyntax BuildSyncRunMethod(BlockSyntax body)
    {
        var method = SyntaxFactory
            .MethodDeclaration(
                SyntaxFactory.ParseTypeName("object?"),
                SyntaxFactory.Identifier("Run"))
            .WithParameterList(SyntaxFactory.ParseParameterList("(object __ctx__)"))
            .WithBody(body);

        return AddPublicStaticModifiers(method);
    }

    private static BlockSyntax BuildRunBody(
        string contextVarName,
        string contextTypeFullName,
        ExpressionSyntax expressionNode,
        IReadOnlyList<string> stubs,
        bool useAsync)
    {
        var stmts = new List<StatementSyntax>();

        // var {contextVarName} = ({contextTypeFullName})(object)__ctx__;
        stmts.Add(Parse($"var {contextVarName} = ({contextTypeFullName})(object)__ctx__;"));

        // Optional type stubs (auto-declared locals from retry loop).
        foreach (var stub in stubs)
            stmts.Add(Parse(stub.TrimEnd(';') + ";"));

        stmts.Add(Parse("string? __captureSkipReason = null;"));
        stmts.Add(Parse("string? __captureError = null;"));
        stmts.Add(Parse(
            $"var __captureInstalled = __QueryLensOfflineCapture__.TryInstall({contextVarName}, out __captureSkipReason);"));
        stmts.Add(Parse("var __captured = Array.Empty<__QueryLensCapturedSqlCommand__>();"));
        stmts.Add(Parse("object? __query = null;"));
        stmts.Add(Parse(
            "using var __scope = __captureInstalled ? __QueryLensSqlCaptureScope__.Begin() : null;"));

        stmts.Add(BuildExecutionTryCatch(expressionNode, useAsync));

        stmts.Add(Parse("""
            return new __QueryLensExecutionResult__
            {
                Queryable = __query,
                CaptureSkipReason = __captureSkipReason,
                CaptureError = __captureError,
                Commands = __captured,
            };
            """));

        return SyntaxFactory.Block(stmts);
    }

    // ─── Try/catch/finally ────────────────────────────────────────────────────

    private static TryStatementSyntax BuildExecutionTryCatch(
        ExpressionSyntax expressionNode,
        bool useAsync)
    {
        var tryStmts = new List<StatementSyntax>();

        // Build the cast to object? and assign to __query
        var queryAssignment = BuildQueryAssignment(expressionNode);
        tryStmts.Add(queryAssignment);

        // Unwrap Task if needed
        tryStmts.Add(
            useAsync
                ? Parse("__query = await UnwrapTaskAsync(__query, ct).ConfigureAwait(false);")
                : Parse("__query = UnwrapTask(__query);"));

        // Enumerate queryable if capture is installed
        tryStmts.Add(Parse(
            "if (__captureInstalled && __query is System.Collections.IEnumerable __enumerable)" +
            " EnumerateQueryable(__enumerable);"));

        var tryBlock = SyntaxFactory.Block(tryStmts);
        var catchClause = BuildExceptionCatchClause();
        var finallyClause = BuildFinallyClause();

        return SyntaxFactory
            .TryStatement()
            .WithBlock(tryBlock)
            .WithCatches(SyntaxFactory.SingletonList(catchClause))
            .WithFinally(finallyClause);
    }

    /// <summary>
    /// Builds the assignment statement: __query = (object?)({expressionNode});
    /// </summary>
    private static ExpressionStatementSyntax BuildQueryAssignment(ExpressionSyntax expressionNode)
    {
        var cast = SyntaxFactory.CastExpression(
            SyntaxFactory.NullableType(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword))),
            SyntaxFactory.ParenthesizedExpression(expressionNode));

        return SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName("__query"),
                cast));
    }

    /// <summary>
    /// Builds the exception catch clause that captures error details.
    /// </summary>
    private static CatchClauseSyntax BuildExceptionCatchClause()
    {
        return SyntaxFactory
            .CatchClause()
            .WithDeclaration(
                SyntaxFactory
                    .CatchDeclaration(SyntaxFactory.IdentifierName("Exception"))
                    .WithIdentifier(SyntaxFactory.Identifier("ex")))
            .WithBlock(
                SyntaxFactory.Block(
                    Parse("""__captureError = ex.GetType().Name + ": " + ex.Message;""")));
    }

    /// <summary>
    /// Builds the finally clause that captures the commands from the scope.
    /// </summary>
    private static FinallyClauseSyntax BuildFinallyClause()
    {
        return SyntaxFactory.FinallyClause(
            SyntaxFactory.Block(
                Parse("if (__captureInstalled) __captured = __scope!.GetCommands();")));
    }

    // ─── Static helper methods ────────────────────────────────────────────────

    private static IEnumerable<MethodDeclarationSyntax> ParseMethods(string source)
    {
        var root = CSharpSyntaxTree
            .ParseText($"class __D__ {{ {source} }}", SParseOptions)
            .GetRoot();
        return root.DescendantNodes().OfType<MethodDeclarationSyntax>();
    }

    private static StatementSyntax Parse(string text) =>
        SyntaxFactory.ParseStatement(text, options: SParseOptions);

    // ─── Helper method source templates ──────────────────────────────────────

    private const string AsyncHelpersSource =
        """
        private static async System.Threading.Tasks.Task<object?> UnwrapTaskAsync(
            object? value, System.Threading.CancellationToken ct)
        {
            if (value is not System.Threading.Tasks.Task task)
                return value;
            await task.WaitAsync(ct).ConfigureAwait(false);
            var resultProp = value.GetType().GetProperty("Result");
            return resultProp?.GetValue(value);
        }

        private static void EnumerateQueryable(System.Collections.IEnumerable enumerable)
        {
            var enumerator = enumerable.GetEnumerator();
            try
            {
                var guard = 0;
                while (guard++ < 32 && enumerator.MoveNext()) { }
            }
            finally
            {
                (enumerator as System.IDisposable)?.Dispose();
            }
        }
        """;

    private const string SyncHelpersSource =
        """
        private static object? UnwrapTask(object? value)
        {
            if (value is not System.Threading.Tasks.Task task)
                return value;
            task.GetAwaiter().GetResult();
            var resultProp = value.GetType().GetProperty("Result");
            return resultProp?.GetValue(value);
        }

        private static void EnumerateQueryable(System.Collections.IEnumerable enumerable)
        {
            var enumerator = enumerable.GetEnumerator();
            try
            {
                var guard = 0;
                while (guard++ < 32 && enumerator.MoveNext()) { }
            }
            finally
            {
                (enumerator as System.IDisposable)?.Dispose();
            }
        }
        """;
}
