using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

public static partial class MethodQueryInliner
{
    private static ExpressionSyntax ReplaceInvocationInExpression(
        ExpressionSyntax parsedExpression,
        InvocationExpressionSyntax invocationToReplace,
        ExpressionSyntax replacement)
    {
        if (ReferenceEquals(parsedExpression, invocationToReplace))
        {
            return replacement;
        }

        var wrappedReplacement = SyntaxFactory.ParenthesizedExpression(replacement.WithoutTrivia());
        var rewritten = parsedExpression.ReplaceNode(invocationToReplace, wrappedReplacement);
        return rewritten.WithoutTrivia();
    }

    private static bool TryParseTopLevelInvocation(
        string expression,
        out ExpressionSyntax parsedExpression,
        out InvocationExpressionSyntax invocation,
        out string rootName,
        out string methodName)
    {
        parsedExpression = null!;
        invocation = null!;
        rootName = string.Empty;
        methodName = string.Empty;

        if (SyntaxFactory.ParseExpression(expression) is not ExpressionSyntax parsed)
        {
            return false;
        }

        var targetInvocation = parsed
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Where(i => i.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax })
            .OrderBy(i => i.SpanStart)
            .FirstOrDefault();

        if (targetInvocation is null)
        {
            return false;
        }

        if (targetInvocation.Expression is not MemberAccessExpressionSyntax memberAccess
            || memberAccess.Expression is not IdentifierNameSyntax rootIdentifier)
        {
            return false;
        }

        parsedExpression = parsed;
        invocation = targetInvocation;
        rootName = rootIdentifier.Identifier.ValueText;
        methodName = memberAccess.Name.Identifier.ValueText;
        return true;
    }

    private static string? FindSearchRoot(string sourceFilePath)
    {
        var current = Path.GetDirectoryName(sourceFilePath);

        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.GetFiles(current, "*.sln").Length > 0)
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        current = Path.GetDirectoryName(sourceFilePath);
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.GetFiles(current, "*.csproj").Length > 0)
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        return null;
    }

    private static IEnumerable<(string FilePath, MethodDeclarationSyntax Method)> FindMethodCandidates(
        string searchRoot,
        string sourceFilePath,
        string sourceText,
        string methodName,
        int argumentCount)
    {
        foreach (var filePath in EnumerateCSharpFiles(searchRoot, sourceFilePath))
        {
            string text;
            try
            {
                text = string.Equals(filePath, sourceFilePath, StringComparison.OrdinalIgnoreCase)
                    ? sourceText
                    : File.ReadAllText(filePath);
            }
            catch
            {
                continue;
            }

            var tree = CSharpSyntaxTree.ParseText(text);
            var root = tree.GetRoot();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (!string.Equals(method.Identifier.ValueText, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                var parameterCount = method.ParameterList.Parameters.Count;
                if (parameterCount < argumentCount)
                {
                    continue;
                }

                yield return (filePath, method);
            }
        }
    }

    private static IEnumerable<string> EnumerateCSharpFiles(string searchRoot, string preferredSourceFile)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(preferredSourceFile))
        {
            yielded.Add(preferredSourceFile);
            yield return preferredSourceFile;
        }

        var dirs = new Stack<string>();
        dirs.Push(searchRoot);

        while (dirs.Count > 0)
        {
            var current = dirs.Pop();

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var subDir in subDirs)
            {
                var name = Path.GetFileName(subDir);
                if (ShouldSkipDirectory(name))
                {
                    continue;
                }

                dirs.Push(subDir);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current, "*.cs");
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (yielded.Add(file))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool ShouldSkipDirectory(string name)
    {
        return string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, ".vs", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, ".vscode", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreCandidate(MethodDeclarationSyntax method, IReadOnlyList<ExpressionSyntax> arguments)
    {
        var parameters = method.ParameterList.Parameters;
        if (arguments.Count > parameters.Count)
        {
            return -1;
        }

        var score = 0;

        if (arguments.Count == parameters.Count)
        {
            score += 20;
        }
        else
        {
            var remaining = parameters.Skip(arguments.Count);
            if (remaining.Any(p => p.Default == null))
            {
                return -1;
            }

            score += 10;
        }

        for (var i = 0; i < arguments.Count; i++)
        {
            var parameter = parameters[i];
            var parameterType = parameter.Type?.ToString() ?? string.Empty;
            var argument = arguments[i];

            if (parameterType.Contains("Expression", StringComparison.Ordinal) &&
                (argument is LambdaExpressionSyntax || argument is AnonymousMethodExpressionSyntax))
            {
                score += 5;
            }

            if (parameterType.Contains("CancellationToken", StringComparison.Ordinal))
            {
                score += argument is IdentifierNameSyntax id &&
                         (string.Equals(id.Identifier.ValueText, "ct", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(id.Identifier.ValueText, "cancellationToken", StringComparison.OrdinalIgnoreCase))
                    ? 4
                    : 1;
            }
        }

        return score;
    }
}
