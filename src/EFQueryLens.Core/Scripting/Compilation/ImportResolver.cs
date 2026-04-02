using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Core.Scripting.Evaluation;

internal static partial class ImportResolver
{
    [GeneratedRegex(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)\s*\(")]
    private static partial Regex TopLevelInvocationRegex();

    [GeneratedRegex(@"\.\s*Find(Async)?\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex FindInvocationRegex();

    private sealed record MissingExtensionRequest(
        string MethodName,
        HashSet<string> ReceiverTypeNames,
        int? InvocationArgumentCount);

    private static bool IsExtensionImportRecoveryDiagnostic(Diagnostic diagnostic) =>
        diagnostic.Id is "CS1061" or "CS1929" or "CS7036";

    internal static IReadOnlyList<string> InferMissingExtensionStaticImports(
        IEnumerable<Diagnostic> errors,
        CSharpCompilation compilation,
        IEnumerable<Assembly> assemblies)
    {
        var requested = new List<MissingExtensionRequest>();
        var requestKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var error in errors.Where(IsExtensionImportRecoveryDiagnostic))
        {
            if (!TryExtractMissingExtensionRequest(error, compilation, out var request))
                continue;

            var key = request.MethodName + "|"
                + string.Join("|", request.ReceiverTypeNames.OrderBy(static x => x, StringComparer.Ordinal))
                + "|"
                + (request.InvocationArgumentCount?.ToString() ?? "_");
            if (!requestKeys.Add(key))
            {
                continue;
            }

            requested.Add(request);
        }

        if (requested.Count == 0)
            return [];

        var imports = new HashSet<string>(StringComparer.Ordinal);

        foreach (var asm in assemblies)
        {
            Type[] allTypes;
            try
            {
                allTypes = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException rtle)
            {
                allTypes = rtle.Types.Where(t => t is not null).Cast<Type>().ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var type in allTypes)
            {
                if (!(type.IsAbstract && type.IsSealed))
                    continue;

                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
                }
                catch
                {
                    continue;
                }

                foreach (var method in methods)
                {
                    if (!method.IsDefined(typeof(ExtensionAttribute), inherit: false))
                        continue;

                    var matchingRequests = requested
                        .Where(r => string.Equals(r.MethodName, method.Name, StringComparison.Ordinal))
                        .Where(r => IsExtensionMethodApplicableToInvocation(method, r.InvocationArgumentCount))
                        .ToArray();
                    if (matchingRequests.Length == 0)
                        continue;

                    var firstParam = method.GetParameters().FirstOrDefault()?.ParameterType;
                    if (firstParam is null || !matchingRequests.Any(r => IsReceiverNameMatch(firstParam, r.ReceiverTypeNames)))
                        continue;

                    if (!string.IsNullOrWhiteSpace(type.FullName))
                        imports.Add(type.FullName.Replace('+', '.'));
                }
            }
        }

        return imports.ToArray();
    }

    private static bool TryExtractMissingExtensionRequest(
        Diagnostic diagnostic,
        CSharpCompilation compilation,
        out MissingExtensionRequest request)
    {
        request = null!;

        if (!diagnostic.Location.IsInSource || diagnostic.Location.SourceTree is null)
            return false;

        var sourceTree = diagnostic.Location.SourceTree;
        var root = sourceTree.GetRoot();
        var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

        var invocation = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        var memberAccess = node as MemberAccessExpressionSyntax
            ?? invocation?.Expression as MemberAccessExpressionSyntax
            ?? node.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault();
        if (memberAccess is null)
            return false;

        var methodName = memberAccess.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(methodName))
            return false;

        var semanticModel = compilation.GetSemanticModel(sourceTree, ignoreAccessibility: true);
        var receiverExpression = memberAccess.Expression;
        var receiverType = semanticModel.GetTypeInfo(receiverExpression).Type
            ?? semanticModel.GetTypeInfo(receiverExpression).ConvertedType;
        if (receiverType is null)
            return false;

        var receiverTypeNames = BuildReceiverTypeNameSet(receiverType);
        if (receiverTypeNames.Count == 0)
            return false;

        var invocationArgumentCount = invocation?.ArgumentList.Arguments.Count;

        request = new MissingExtensionRequest(methodName, receiverTypeNames, invocationArgumentCount);
        return true;
    }

    private static bool IsExtensionMethodApplicableToInvocation(MethodInfo method, int? invocationArgumentCount)
    {
        if (invocationArgumentCount is null)
            return true;

        var parameters = method.GetParameters();
        if (parameters.Length == 0)
            return false;

        var extensionParameters = parameters.Skip(1).ToArray();
        var hasParamsArray = extensionParameters.Any(IsParamsArrayParameter);
        var requiredCount = extensionParameters.Count(p => !p.IsOptional && !IsParamsArrayParameter(p));
        var maxCount = hasParamsArray ? int.MaxValue : extensionParameters.Length;
        var provided = invocationArgumentCount.Value;

        return provided >= requiredCount && provided <= maxCount;
    }

    private static bool IsParamsArrayParameter(ParameterInfo parameter)
        => parameter.GetCustomAttribute<ParamArrayAttribute>() is not null;

    private static HashSet<string> BuildReceiverTypeNameSet(ITypeSymbol typeSymbol)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        AddTypeAndHierarchy(typeSymbol, result, visited);
        return result;
    }

    private static void AddTypeAndHierarchy(
        ITypeSymbol? typeSymbol,
        ISet<string> target,
        ISet<ITypeSymbol> visited)
    {
        if (typeSymbol is null || !visited.Add(typeSymbol))
            return;

        if (!string.IsNullOrWhiteSpace(typeSymbol.Name))
            target.Add(typeSymbol.Name);

        if (typeSymbol is INamedTypeSymbol named)
        {
            if (!string.IsNullOrWhiteSpace(named.MetadataName))
                target.Add(named.MetadataName);

            var fullName = NormalizeTypeName(named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            if (!string.IsNullOrWhiteSpace(fullName))
                target.Add(fullName);

            var originalFullName = NormalizeTypeName(named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            if (!string.IsNullOrWhiteSpace(originalFullName))
                target.Add(originalFullName);

            if (!string.IsNullOrWhiteSpace(named.OriginalDefinition.Name))
                target.Add(named.OriginalDefinition.Name);

            if (!string.IsNullOrWhiteSpace(named.OriginalDefinition.MetadataName))
                target.Add(named.OriginalDefinition.MetadataName);

            AddTypeAndHierarchy(named.BaseType, target, visited);
            foreach (var i in named.AllInterfaces)
                AddTypeAndHierarchy(i, target, visited);
        }
    }

    private static string NormalizeTypeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var normalized = value.Trim();
        if (normalized.StartsWith("global::", StringComparison.Ordinal))
            normalized = normalized["global::".Length..];

        return normalized.Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static bool IsReceiverNameMatch(Type parameterType, IReadOnlySet<string> receiverTypeNames)
    {
        if (parameterType.IsByRef)
            parameterType = parameterType.GetElementType() ?? parameterType;

        if (receiverTypeNames.Contains(parameterType.Name)
            || (!string.IsNullOrWhiteSpace(parameterType.FullName)
                && receiverTypeNames.Contains(parameterType.FullName)))
        {
            return true;
        }

        if (parameterType.IsGenericType)
        {
            var genericName = parameterType.GetGenericTypeDefinition().Name;
            if (receiverTypeNames.Contains(genericName))
                return true;

            var tick = genericName.IndexOf('`');
            if (tick > 0)
            {
                genericName = genericName[..tick];
            }

            if (receiverTypeNames.Contains(genericName))
            {
                return true;
            }
        }

        return false;
    }

    internal static string? TryExtractRootIdentifier(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return null;

        try
        {
            var parsed = SyntaxFactory.ParseExpression(expression);
            var current = parsed;

            while (true)
            {
                switch (current)
                {
                    case InvocationExpressionSyntax invocation
                        when invocation.Expression is MemberAccessExpressionSyntax memberAccess:
                        current = memberAccess.Expression;
                        continue;

                    case MemberAccessExpressionSyntax member:
                        current = member.Expression;
                        continue;

                    case ParenthesizedExpressionSyntax parenthesized:
                        current = parenthesized.Expression;
                        continue;

                    case CastExpressionSyntax cast:
                        current = cast.Expression;
                        continue;

                    case IdentifierNameSyntax identifier:
                        return identifier.Identifier.ValueText;

                    default:
                        return null;
                }
            }
        }
        catch
        {
            return null;
        }
    }

    internal static bool IsUnsupportedTopLevelMethodInvocation(string expression, string ctxVar)
    {
        var m = TopLevelInvocationRegex().Match(expression);
        if (!m.Success)
            return false;

        if (string.Equals(m.Groups[1].Value, ctxVar, StringComparison.Ordinal)
            && string.Equals(m.Groups[2].Value, "Set", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    internal static bool ContainsFindInvocation(string expression) =>
        FindInvocationRegex().IsMatch(expression);

}
