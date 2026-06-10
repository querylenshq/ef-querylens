using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

public static partial class LspSyntaxHelper
{
    public static string? TryExtractLinqExpression(
        string sourceText,
        int line,
        int character,
        out string? contextVariableName,
        IReadOnlyList<SyntaxNode>? additionalRoots = null,
        string? sourceFilePath = null)
    {
        contextVariableName = null;

        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetRoot();

        var textLines = sourceText.Split('\n');
        if (line >= textLines.Length) return null;

        var textLine = textLines[line];
        if (character > textLine.Length) return null;

        // Find the absolute position from Line/Char
        var position = tree.GetText().Lines[line].Start + character;

        // Find the node at the cursor position
        var node = root.FindToken(position).Parent;

        // Tier 0: compose helper DbContext query + call-site Expression args before
        // extracting inner lambda-scoped LINQ fragments (e.g. navigation .Where inside
        // a Select projection passed to GetApplicationByIdAsync).
        if (node is not null
            && TryComposeFromEnclosingCallSite(
                root,
                node,
                position,
                sourceText,
                sourceFilePath,
                additionalRoots,
                out var composedExpression,
                out var composedContextVariableName))
        {
            contextVariableName = composedContextVariableName;
            return composedExpression;
        }

        // Walk up until we find an InvocationExpression (like .Where() or .ToList())
        // or a MemberAccessExpression (like db.Orders)
        var invocation = node?.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        var memberAccess = node?.FirstAncestorOrSelf<MemberAccessExpressionSyntax>();

        ExpressionSyntax? targetExpression = invocation ?? (ExpressionSyntax?)memberAccess;

        if (targetExpression == null)
            return null;

        // Walk to the topmost invocation/member access, including any terminal call
        // (Count, ToList, FirstOrDefaultAsync, ExecuteDeleteAsync, etc.) so the engine
        // receives the exact expression the app runs and generates the real SQL.
        while (targetExpression.Parent is MemberAccessExpressionSyntax or InvocationExpressionSyntax)
        {
            targetExpression = (ExpressionSyntax)targetExpression.Parent;
        }

        // Guard: reject expressions that are not LINQ query chains.
        // Without this, hovering inside a lambda argument of a non-LINQ method call
        // (e.g. "x => new Dto{...}" passed to GetFooAsync(id, x => new Dto{...}, ct))
        // causes the entire call site to be extracted as the LINQ expression, with the
        // method name mis-identified as the DbContext variable name. The engine then
        // declares a variable using that name and later tries to invoke it as a method,
        // producing CS0149: Method name expected.
        // GetInvocationChainMethodNames only yields for member-access chains (a.b.c()),
        // so a bare call like GetFooAsync(...) yields nothing → IsLikelyQueryChain = false.
        if (targetExpression is InvocationExpressionSyntax finalInvocation
            && !IsLikelyQueryChain(finalInvocation))
        {
            if (TryExtractFromExpressionParameterHelperCall(
                    root,
                    finalInvocation,
                    position,
                    out var synthesizedExpression,
                    out var synthesizedContextVariableName,
                    additionalRoots)
                || TryExtractFromExpressionParameterHelperCallWithLookup(
                    root,
                    finalInvocation,
                    position,
                    sourceText,
                    sourceFilePath,
                    out synthesizedExpression,
                    out synthesizedContextVariableName))
            {
                contextVariableName = synthesizedContextVariableName;
                return synthesizedExpression;
            }

            // The cursor is inside a nested call (e.g. a predicate method inside a lambda
            // argument: "w.IsNotDeleted()" inside ".Where(w => w.IsNotDeleted())").
            // Walk up through ancestors to find a containing LINQ query chain — this
            // handles hovering on any token within a .Where(…) or .Select(…) lambda
            // in Visual Studio / Rider, where the QuickInfo trigger fires on the exact
            // token under the cursor rather than the method name.
            var outerChain = node?.AncestorsAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Select(GetOutermostInvocationChain)
                .FirstOrDefault(IsLikelyQueryChain);

            if (outerChain is null)
            {
                return null;
            }

            targetExpression = outerChain;
        }

        // If the outermost chain is chained on the result of an await expression
        // (e.g. "(await query.ToListAsync()).ToList()"), strip the outer in-memory
        // part and keep only the awaited EF query.  The runner template already
        // handles Task<T> via UnwrapTask; keeping the await would cause CS4032
        // in the generated synchronous scaffold.
        targetExpression = StripOuterAwaitChain(targetExpression);

        // Inline local IQueryable variables for non-terminal chains too, so
        // expressions like auditTrailQuery.ApplyPaging(...).ToListAsync(...) are
        // rooted back to dbContext.* and keep DbContext discovery deterministic.
        if (invocation is not null)
        {
            targetExpression = TryInlineLocalQueryRoot(targetExpression, invocation);
        }

        targetExpression = StripTransparentQueryableCasts(targetExpression);

        // Identify the root variable from the left-most chain segment.
        // Using DescendantNodes().FirstOrDefault() can pick lambda identifiers
        // (e.g. "s") depending on cursor position and trivia layout.
        contextVariableName = TryExtractRootContextVariable(targetExpression)
            ?? targetExpression.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Select(i => i.Identifier.Text)
                .FirstOrDefault();

        // Tier 2: reject lambda-scoped fragments whose root is not a DbContext variable.
        // Statement-level indirection (e.g. services.Context.CatalogItems) is still allowed.
        if (!LooksLikeDbContextRoot(contextVariableName))
        {
            if (node is not null
                && TryComposeFromEnclosingCallSite(
                    root,
                    node,
                    position,
                    sourceText,
                    sourceFilePath,
                    additionalRoots,
                    out composedExpression,
                    out composedContextVariableName))
            {
                contextVariableName = composedContextVariableName;
                return composedExpression;
            }

            if (IsInsideLambda(targetExpression))
            {
                contextVariableName = null;
                return null;
            }
        }

        return targetExpression.ToString();
    }

    private static bool TryExtractFromExpressionParameterHelperCallWithLookup(
        SyntaxNode root,
        InvocationExpressionSyntax invocation,
        int cursorPosition,
        string sourceText,
        string? sourceFilePath,
        out string expression,
        out string? contextVariableName)
    {
        expression = string.Empty;
        contextVariableName = null;

        if (string.IsNullOrWhiteSpace(sourceFilePath))
        {
            return false;
        }

        if (!ShouldAttemptCrossFileHelperLookup(invocation, cursorPosition))
        {
            return false;
        }

        var methodName = GetInvokedMethodName(invocation);
        if (string.IsNullOrWhiteSpace(methodName))
        {
            return false;
        }

        var receiver = TryExtractReceiverIdentifier(invocation);
        var receiverType = receiver is not null
            ? TryResolveDeclaredTypeName(sourceText, receiver, sourceFilePath)
            : null;

        var helperRoots = ProjectSourceHelper.TryResolveHelperMethodRoots(
            sourceFilePath,
            sourceText,
            methodName,
            receiverType);

        if (helperRoots.Count == 0)
        {
            return false;
        }

        return TryExtractFromExpressionParameterHelperCall(
            root,
            invocation,
            cursorPosition,
            out expression,
            out contextVariableName,
            helperRoots);
    }

    private static string? TryExtractReceiverIdentifier(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression.ToString(),
            _ => null,
        };

    public static SourceUsingContext ExtractUsingContext(string sourceText, string? sourceFilePath = null)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var context = CollectUsingContextFromRoot(tree.GetRoot(), includeFileNamespaces: true);

        if (!string.IsNullOrWhiteSpace(sourceFilePath))
        {
            MergeProjectUsingContext(sourceFilePath, context);
        }

        return context.ToSourceUsingContext();
    }

    private sealed class MutableUsingContext
    {
        public List<string> Imports { get; } = [];
        public HashSet<string> ImportSet { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Aliases { get; } = new(StringComparer.Ordinal);
        public List<string> StaticTypes { get; } = [];
        public HashSet<string> StaticSet { get; } = new(StringComparer.Ordinal);

        public SourceUsingContext ToSourceUsingContext()
            => new(Imports, Aliases, StaticTypes);
    }

    private static MutableUsingContext CollectUsingContextFromRoot(
        SyntaxNode root,
        bool includeFileNamespaces,
        bool globalUsingsOnly = false)
    {
        var context = new MutableUsingContext();

        foreach (var usingDirective in root.ChildNodes().OfType<UsingDirectiveSyntax>())
        {
            if (globalUsingsOnly && usingDirective.GlobalKeyword.IsKind(SyntaxKind.None))
            {
                continue;
            }

            AddUsingDirective(context, usingDirective);
        }

        if (!globalUsingsOnly)
        {
            foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                if (usingDirective.Parent is CompilationUnitSyntax)
                {
                    continue;
                }

                AddUsingDirective(context, usingDirective);
            }
        }

        if (includeFileNamespaces)
        {
            foreach (var namespaceDecl in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            {
                var ns = namespaceDecl.Name.ToString();
                if (string.IsNullOrWhiteSpace(ns))
                {
                    continue;
                }

                if (context.ImportSet.Add(ns))
                {
                    context.Imports.Add(ns);
                }
            }
        }

        return context;
    }

    private static void AddUsingDirective(MutableUsingContext context, UsingDirectiveSyntax usingDirective)
    {
        var name = usingDirective.Name?.ToString();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (usingDirective.Alias is { Name.Identifier.ValueText: { Length: > 0 } aliasName })
        {
            context.Aliases.TryAdd(aliasName, name);
            return;
        }

        if (!usingDirective.StaticKeyword.IsKind(SyntaxKind.None))
        {
            if (context.StaticSet.Add(name))
            {
                context.StaticTypes.Add(name);
            }

            return;
        }

        if (context.ImportSet.Add(name))
        {
            context.Imports.Add(name);
        }
    }

    private static void MergeProjectUsingContext(string sourceFilePath, MutableUsingContext context)
    {
        var projectDir = AssemblyResolver.TryGetProjectDirectory(sourceFilePath);
        if (projectDir is null)
        {
            return;
        }

        var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var dir = projectDir; dir is not null && visitedDirs.Add(dir); dir = Directory.GetParent(dir)?.FullName)
        {
            foreach (var root in GetGlobalUsingRootsInDirectory(dir))
            {
                var globalContext = CollectUsingContextFromRoot(root, includeFileNamespaces: false, globalUsingsOnly: true);
                MergeUsingContext(context, globalContext);
            }

            foreach (var csproj in Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly))
            {
                MergeCsprojUsings(csproj, context);
            }

            foreach (var buildProps in Directory.GetFiles(dir, "Directory.Build.*", SearchOption.TopDirectoryOnly))
            {
                MergeCsprojUsings(buildProps, context);
            }
        }
    }

    private static void MergeUsingContext(MutableUsingContext target, MutableUsingContext source)
    {
        foreach (var import in source.Imports)
        {
            if (target.ImportSet.Add(import))
            {
                target.Imports.Add(import);
            }
        }

        foreach (var kvp in source.Aliases)
        {
            target.Aliases.TryAdd(kvp.Key, kvp.Value);
        }

        foreach (var staticType in source.StaticTypes)
        {
            if (target.StaticSet.Add(staticType))
            {
                target.StaticTypes.Add(staticType);
            }
        }
    }

    private static IEnumerable<SyntaxNode> GetGlobalUsingRootsInDirectory(string projectDir)
    {
        foreach (var file in Directory.GetFiles(projectDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            if (IsBuildOutputPath(file))
            {
                continue;
            }

            var fileName = Path.GetFileName(file);
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            if (!fileName.Contains("GlobalUsings", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fileName, "Usings.cs", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("global using", StringComparison.Ordinal))
            {
                continue;
            }

            SyntaxNode root;
            try
            {
                root = CSharpSyntaxTree.ParseText(text).GetRoot();
            }
            catch
            {
                continue;
            }

            yield return root;
        }
    }

    private static bool IsBuildOutputPath(string filePath)
    {
        var normalized = filePath.Replace('/', Path.DirectorySeparatorChar);
        return normalized.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static void MergeCsprojUsings(string csprojPath, MutableUsingContext context)
    {
        string text;
        try
        {
            text = File.ReadAllText(csprojPath);
        }
        catch
        {
            return;
        }

        foreach (Match match in CsprojUsingRegex().Matches(text))
        {
            var include = match.Groups["include"].Value.Trim();
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            if (match.Groups["alias"].Success)
            {
                context.Aliases.TryAdd(match.Groups["alias"].Value.Trim(), include);
                continue;
            }

            if (match.Groups["static"].Success)
            {
                if (context.StaticSet.Add(include))
                {
                    context.StaticTypes.Add(include);
                }

                continue;
            }

            if (context.ImportSet.Add(include))
            {
                context.Imports.Add(include);
            }
        }
    }

    [GeneratedRegex(
        @"<Using\s+Include\s*=\s*""(?<include>[^""]+)""(?:\s+Alias\s*=\s*""(?<alias>[^""]+)""|\s+Static\s*=\s*""(?<static>true)"")?\s*/?>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CsprojUsingRegex();
}
