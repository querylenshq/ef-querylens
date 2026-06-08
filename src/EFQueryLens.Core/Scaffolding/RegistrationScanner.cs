using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Core.Scaffolding;

/// <summary>
/// Roslyn scan for <c>AddDbContext</c>/<c>AddDbContextPool</c>/<c>AddDbContextFactory</c>
/// registrations across the host project and, when available, all projects in the solution.
/// </summary>
public static class RegistrationScanner
{
    private static readonly string[] RegistrationMethodNames =
    [
        "AddDbContext",
        "AddDbContextPool",
        "AddDbContextFactory",
    ];

    public static IReadOnlyList<DbContextRegistration> Scan(string hostProjectDirectory)
    {
        if (string.IsNullOrWhiteSpace(hostProjectDirectory) || !Directory.Exists(hostProjectDirectory))
        {
            return [];
        }

        var projectDirectories = ResolveScanDirectories(hostProjectDirectory);
        var registrations = new List<DbContextRegistration>();

        foreach (var projectDir in projectDirectories)
        {
            foreach (var file in EnumerateSourceFiles(projectDir))
            {
                try
                {
                    registrations.AddRange(ScanFile(file));
                }
                catch
                {
                    // Skip unreadable or unparseable files.
                }
            }
        }

        return Deduplicate(registrations);
    }

    internal static IReadOnlyList<string> ResolveScanDirectories(string hostProjectDirectory)
    {
        var normalizedHost = Path.GetFullPath(hostProjectDirectory);
        var slnFile = FindSolutionFile(normalizedHost);
        if (slnFile is null)
        {
            return [normalizedHost];
        }

        var slnDir = Path.GetDirectoryName(slnFile)!;
        var projects = ParseSolutionProjects(slnFile)
            .Select(p => Path.GetFullPath(Path.Combine(slnDir, p)))
            .Where(File.Exists)
            .Select(Path.GetDirectoryName!)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return projects.Count > 0
            ? projects.Where(static d => d is not null).Select(static d => d!).ToList()
            : [normalizedHost];
    }

    private static IEnumerable<DbContextRegistration> ScanFile(string filePath)
    {
        var text = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetCompilationUnitRoot();
        var usings = root.Usings
            .Select(u => u.Name?.ToString())
            .Where(static n => !string.IsNullOrWhiteSpace(n))
            .Select(static n => n!)
            .ToList();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!TryGetRegistrationMethodName(invocation, out var methodName))
            {
                continue;
            }

            if (!TryGetContextTypeName(invocation, methodName, out var contextTypeName))
            {
                continue;
            }

            if (!TryExtractOptionsLambda(invocation, out var builderParameter, out var optionsChain))
            {
                continue;
            }

            yield return new DbContextRegistration
            {
                ContextTypeName = contextTypeName,
                BuilderParameterName = builderParameter,
                OptionsChain = optionsChain,
                Usings = usings,
                SourceFilePath = filePath,
            };
        }
    }

    private static bool TryGetRegistrationMethodName(InvocationExpressionSyntax invocation, out string methodName)
    {
        methodName = string.Empty;

        switch (invocation.Expression)
        {
            case MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName }:
                methodName = genericName.Identifier.Text;
                break;
            case MemberAccessExpressionSyntax { Name: IdentifierNameSyntax identifierName }:
                methodName = identifierName.Identifier.Text;
                break;
            default:
                return false;
        }

        return RegistrationMethodNames.Contains(methodName, StringComparer.Ordinal);
    }

    private static bool TryGetContextTypeName(
        InvocationExpressionSyntax invocation,
        string methodName,
        out string contextTypeName)
    {
        contextTypeName = string.Empty;

        if (invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName }
            && genericName.TypeArgumentList.Arguments.Count > 0)
        {
            contextTypeName = genericName.TypeArgumentList.Arguments[0].ToString().Trim();
            return !string.IsNullOrWhiteSpace(contextTypeName);
        }

        // AddDbContext(services, typeof(MyContext), ...) fallback.
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is TypeOfExpressionSyntax typeOf)
            {
                contextTypeName = typeOf.Type.ToString().Trim();
                return !string.IsNullOrWhiteSpace(contextTypeName);
            }
        }

        return methodName is "AddDbContext" or "AddDbContextPool" or "AddDbContextFactory"
               && TryGetContextTypeFromFactoryLambda(invocation, out contextTypeName);
    }

    private static bool TryGetContextTypeFromFactoryLambda(
        InvocationExpressionSyntax invocation,
        out string contextTypeName)
    {
        contextTypeName = string.Empty;
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is not LambdaExpressionSyntax lambda)
            {
                continue;
            }

            var bodyText = lambda.Body.ToString();
            var match = Regex.Match(
                bodyText,
                @"new\s+([\w.]+)\s*\(",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));

            if (match.Success)
            {
                contextTypeName = match.Groups[1].Value.Trim();
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractOptionsLambda(
        InvocationExpressionSyntax invocation,
        out string builderParameter,
        out string optionsChain)
    {
        builderParameter = string.Empty;
        optionsChain = string.Empty;

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is not LambdaExpressionSyntax lambda)
            {
                continue;
            }

            if (!TryGetBuilderParameter(lambda, out builderParameter))
            {
                continue;
            }

            if (!TryExtractOptionsChain(lambda, builderParameter, out optionsChain))
            {
                continue;
            }

            return optionsChain.Contains("UseSqlServer", StringComparison.Ordinal)
                   || optionsChain.Contains("UseNpgsql", StringComparison.Ordinal)
                   || optionsChain.Contains("UseMySql", StringComparison.Ordinal)
                   || optionsChain.Contains("UseSqlite", StringComparison.Ordinal);
        }

        builderParameter = string.Empty;
        optionsChain = string.Empty;
        return false;
    }

    private static bool TryGetBuilderParameter(LambdaExpressionSyntax lambda, out string builderParameter)
    {
        builderParameter = string.Empty;

        switch (lambda)
        {
            case SimpleLambdaExpressionSyntax simple:
                builderParameter = simple.Parameter.Identifier.Text;
                return !string.IsNullOrWhiteSpace(builderParameter);
            case ParenthesizedLambdaExpressionSyntax parenthesized:
                if (parenthesized.ParameterList.Parameters.Count == 1)
                {
                    builderParameter = parenthesized.ParameterList.Parameters[0].Identifier.Text;
                    return !string.IsNullOrWhiteSpace(builderParameter);
                }

                if (parenthesized.ParameterList.Parameters.Count == 2)
                {
                    builderParameter = parenthesized.ParameterList.Parameters[1].Identifier.Text;
                    return !string.IsNullOrWhiteSpace(builderParameter);
                }

                return false;
            default:
                return false;
        }
    }

    private static bool TryExtractOptionsChain(
        LambdaExpressionSyntax lambda,
        string builderParameter,
        out string optionsChain)
    {
        optionsChain = string.Empty;

        var bodyExpression = lambda.Body switch
        {
            ExpressionSyntax expression => expression,
            BlockSyntax block => block.Statements
                .OfType<ExpressionStatementSyntax>()
                .Select(s => s.Expression)
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault()
                ?? block.Statements
                    .OfType<ReturnStatementSyntax>()
                    .Select(s => s.Expression)
                    .FirstOrDefault(e => e is not null),
            _ => null,
        };

        if (bodyExpression is null)
        {
            return false;
        }

        var bodyText = bodyExpression.ToFullString().Trim();
        optionsChain = StripBuilderPrefix(bodyText, builderParameter);
        return optionsChain.StartsWith(".", StringComparison.Ordinal);
    }

    internal static string StripBuilderPrefix(string bodyText, string builderParameter)
    {
        var trimmed = bodyText.Trim();
        var prefix = builderParameter + ".";
        string chain;
        if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
        {
            chain = trimmed[prefix.Length..].TrimStart();
        }
        else if (trimmed.StartsWith(builderParameter, StringComparison.Ordinal))
        {
            chain = trimmed[builderParameter.Length..].TrimStart().TrimStart('.');
        }
        else if (trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            chain = trimmed.TrimStart('.');
        }
        else
        {
            chain = trimmed;
        }

        return chain.StartsWith(".", StringComparison.Ordinal) ? chain : "." + chain;
    }

    private static IReadOnlyList<DbContextRegistration> Deduplicate(IReadOnlyList<DbContextRegistration> registrations)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DbContextRegistration>();

        foreach (var registration in registrations)
        {
            var key = registration.ContextTypeName + "|" + registration.OptionsChain;
            if (seen.Add(key))
            {
                result.Add(registration);
            }
        }

        return result;
    }

    private static string? FindSolutionFile(string startDirectory)
    {
        var dir = startDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            try
            {
                var slnFiles = Directory.GetFiles(dir, "*.sln");
                if (slnFiles.Length > 0)
                {
                    return slnFiles[0];
                }
            }
            catch
            {
                return null;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    private static IEnumerable<string> ParseSolutionProjects(string slnFile)
    {
        var content = File.ReadAllText(slnFile);
        foreach (Match match in Regex.Matches(
                     content,
                     @"Project\("".+?""\)\s*=\s*"".+?""\s*,\s*""(.+?\.csproj)""",
                     RegexOptions.Multiline,
                     TimeSpan.FromSeconds(5)))
        {
            yield return match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
        }
    }

    private static IEnumerable<string> EnumerateSourceFiles(string projectDirectory)
    {
        if (!Directory.Exists(projectDirectory))
        {
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return file;
        }
    }
}
