using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using EFQueryLens.Core.AssemblyContext;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Core.Scripting.Evaluation;

public sealed partial class QueryEvaluator
{
    private sealed record MissingExtensionRequest(
        string MethodName,
        HashSet<string> ReceiverTypeNames,
        int? InvocationArgumentCount);

    private static readonly Regex _missingMemberDiagnosticPattern =
        new(@"'(.+?)' does not contain a definition for '(.+?)'", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ReservedChainMemberNames = new(StringComparer.Ordinal)
    {
        "Any", "AnyAsync", "All", "AllAsync", "ApplyFilteringAndOrdering", "ApplyPaging", "AsEnumerable",
        "AsNoTracking", "AsNoTrackingWithIdentityResolution", "AsQueryable", "AsSingleQuery", "AsSplitQuery",
        "Average", "AverageAsync", "Cast", "Concat", "Contains", "Count", "CountAsync", "DefaultIfEmpty",
        "Distinct", "DistinctBy", "ElementAt", "ElementAtOrDefault", "EndsWith", "Except", "ExecuteDelete",
        "ExecuteDeleteAsync", "ExecuteUpdate", "ExecuteUpdateAsync", "First", "FirstAsync", "FirstOrDefault",
        "FirstOrDefaultAsync", "GroupBy", "GroupJoin", "Include", "IndexOf", "Intersect", "Join", "Last",
        "LastAsync", "LastIndexOf", "LastOrDefault", "LastOrDefaultAsync", "Like", "LongCount", "LongCountAsync",
        "Max", "MaxAsync", "Min", "MinAsync", "Name", "OfType", "Order", "OrderBy", "OrderByDescending",
        "Remove", "Replace", "Reverse", "Select", "SelectMany", "SequenceEqual", "Single", "SingleAsync",
        "SingleOrDefault", "SingleOrDefaultAsync", "Skip", "SkipWhile", "Split", "StartsWith", "Sum", "SumAsync",
        "Take", "TakeWhile", "ThenBy", "ThenByDescending", "ThenInclude", "ToArray", "ToArrayAsync", "ToDictionary",
        "ToDictionaryAsync", "ToHashSet", "ToHashSetAsync", "ToList", "ToListAsync", "ToLookup", "ToLower",
        "ToLowerInvariant", "ToString", "ToUpper", "ToUpperInvariant", "Trim", "TrimEnd", "TrimStart", "Union",
        "Where", "Zip",
    };

    private static bool IsExtensionImportRecoveryDiagnostic(Diagnostic diagnostic) =>
        diagnostic.Id is "CS1061" or "CS1929" or "CS7036";

    private static IReadOnlyList<string> InferExtensionStaticImportsProactively(
        string expression,
        Type dbContextType,
        IEnumerable<Assembly> userAssemblies)
    {
        var requested = ExtractProactiveExtensionRequestsFromExpression(expression);
        if (requested.Count == 0)
            return [];

        return FindExtensionStaticImports(requested, userAssemblies, dbContextType);
    }

    private static IReadOnlyList<string> InferMissingExtensionStaticImports(
        IEnumerable<Diagnostic> errors,
        CSharpCompilation compilation,
        IEnumerable<Assembly> assemblies,
        Type dbContextType)
    {
        var requested = new List<MissingExtensionRequest>();
        var requestKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var error in errors.Where(IsExtensionImportRecoveryDiagnostic))
        {
            if (!TryExtractMissingExtensionRequest(error, compilation, out var request)
                && !TryExtractMissingExtensionRequestFromMessage(error, out request))
            {
                continue;
            }

            var key = BuildExtensionRequestKey(request);
            if (!requestKeys.Add(key))
            {
                continue;
            }

            requested.Add(request);
        }

        if (requested.Count == 0)
            return [];

        return FindExtensionStaticImports(requested, assemblies, dbContextType);
    }

    private static IReadOnlyList<MissingExtensionRequest> ExtractProactiveExtensionRequestsFromExpression(
        string expression)
    {
        var requested = new List<MissingExtensionRequest>();
        var requestKeys = new HashSet<string>(StringComparer.Ordinal);

        ExpressionSyntax parsed;
        try
        {
            parsed = SyntaxFactory.ParseExpression(expression);
        }
        catch
        {
            return requested;
        }

        foreach (var memberAccess in parsed.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            var methodName = memberAccess.Name.Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(methodName)
                || ReservedChainMemberNames.Contains(methodName)
                || IsDbContextRootMemberAccess(memberAccess))
            {
                continue;
            }

            var invocationArgumentCount = memberAccess.Parent is InvocationExpressionSyntax invocation
                ? invocation.ArgumentList.Arguments.Count
                : (int?)null;

            var request = new MissingExtensionRequest(
                methodName,
                ReceiverTypeNames: new HashSet<string>(StringComparer.Ordinal),
                invocationArgumentCount);

            if (!requestKeys.Add(BuildExtensionRequestKey(request)))
            {
                continue;
            }

            requested.Add(request);
        }

        return requested;
    }

    private static string BuildExtensionRequestKey(MissingExtensionRequest request) =>
        request.MethodName + "|"
        + string.Join("|", request.ReceiverTypeNames.OrderBy(static x => x, StringComparer.Ordinal))
        + "|"
        + (request.InvocationArgumentCount?.ToString() ?? "_");

    private static bool IsDbContextRootMemberAccess(MemberAccessExpressionSyntax memberAccess)
    {
        if (memberAccess.Expression is not IdentifierNameSyntax identifier)
            return false;

        return identifier.Identifier.ValueText is "db" or "_db" or "dbContext" or "_dbContext";
    }

    private static IReadOnlyList<string> FindExtensionStaticImports(
        IReadOnlyList<MissingExtensionRequest> requested,
        IEnumerable<Assembly> assemblies,
        Type dbContextType)
    {
        if (requested.Count == 0)
            return [];

        var entityTypes = CollectDbContextEntityTypes(dbContextType);
        var solutionNamespacePrefix = GetSolutionNamespacePrefix(dbContextType);
        var imports = new HashSet<string>(StringComparer.Ordinal);

        foreach (var request in requested)
        {
            var candidates = new List<(string ContainerFullName, Type ContainerType)>();

            foreach (var asm in assemblies)
            {
                foreach (var type in AssemblyReflection.GetCachedLoadableTypes(asm))
                {
                    if (type.IsGenericTypeDefinition)
                        continue;

                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var method in methods)
                    {
                        if (!IsExtensionLikeStaticMethod(method, type))
                            continue;

                        if (!string.Equals(method.Name, request.MethodName, StringComparison.Ordinal))
                            continue;

                        if (!IsExtensionMethodApplicableToInvocation(method, request.InvocationArgumentCount))
                            continue;

                        var firstParam = method.GetParameters().FirstOrDefault()?.ParameterType;
                        if (firstParam is null)
                            continue;

                        var receiverMatch = request.ReceiverTypeNames.Count > 0
                            ? IsReceiverNameMatch(firstParam, request.ReceiverTypeNames)
                            : IsExtensionApplicableToEntityTypes(firstParam, entityTypes);

                        if (!receiverMatch)
                            continue;

                        if (!IsPublicExtensionContainer(type))
                            continue;

                        if (string.IsNullOrWhiteSpace(type.FullName))
                            continue;

                        candidates.Add((type.FullName.Replace('+', '.'), type));
                    }
                }
            }

            var preferred = PreferExtensionContainer(candidates, solutionNamespacePrefix);
            if (!string.IsNullOrWhiteSpace(preferred))
                imports.Add(preferred);
        }

        return imports.ToArray();
    }

    private static IReadOnlyList<Type> CollectDbContextEntityTypes(Type dbContextType)
    {
        var entityTypes = new List<Type>();

        foreach (var property in dbContextType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.PropertyType.IsGenericType)
                continue;

            var genericDefinition = property.PropertyType.GetGenericTypeDefinition();
            if (!string.Equals(
                    genericDefinition.FullName,
                    "Microsoft.EntityFrameworkCore.DbSet`1",
                    StringComparison.Ordinal))
                continue;

            entityTypes.Add(property.PropertyType.GetGenericArguments()[0]);
        }

        return entityTypes;
    }

    private static string? GetSolutionNamespacePrefix(Type dbContextType)
    {
        var ns = dbContextType.Namespace;
        if (string.IsNullOrWhiteSpace(ns))
            return null;

        var parts = ns.Split('.');
        return parts.Length >= 2
            ? $"{parts[0]}.{parts[1]}"
            : parts[0];
    }

    private static string? PreferExtensionContainer(
        IReadOnlyList<(string ContainerFullName, Type ContainerType)> candidates,
        string? solutionNamespacePrefix)
    {
        if (candidates.Count == 0)
            return null;

        return candidates
            .OrderByDescending(c => ScoreExtensionContainer(c.ContainerType, solutionNamespacePrefix))
            .ThenBy(c => c.ContainerFullName, StringComparer.Ordinal)
            .Select(c => c.ContainerFullName)
            .FirstOrDefault();
    }

    private static int ScoreExtensionContainer(Type containerType, string? solutionNamespacePrefix)
    {
        var score = 0;
        var assemblyName = containerType.Assembly.GetName().Name ?? string.Empty;

        if (!assemblyName.StartsWith("System", StringComparison.Ordinal)
            && !assemblyName.StartsWith("Microsoft", StringComparison.Ordinal))
        {
            score += 100;
        }

        var ns = containerType.Namespace ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(solutionNamespacePrefix)
            && ns.StartsWith(solutionNamespacePrefix, StringComparison.Ordinal))
        {
            score += 50;
        }

        if (ns.Contains("Extensions", StringComparison.OrdinalIgnoreCase))
            score += 10;

        return score;
    }

    private static bool IsPublicExtensionContainer(Type type)
        => type.IsPublic || type.IsNestedPublic;

    private static bool IsExtensionApplicableToEntityTypes(Type parameterType, IReadOnlyList<Type> entityTypes)
    {
        if (entityTypes.Count == 0)
            return false;

        if (parameterType.IsByRef)
            parameterType = parameterType.GetElementType() ?? parameterType;

        foreach (var entityType in entityTypes)
        {
            if (parameterType.IsAssignableFrom(entityType))
                return true;

            foreach (var iface in entityType.GetInterfaces())
            {
                if (parameterType.IsAssignableFrom(iface))
                    return true;
            }
        }

        foreach (var candidate in EnumerateTypeHierarchy(parameterType))
        {
            foreach (var entityType in entityTypes)
            {
                if (candidate.IsAssignableFrom(entityType))
                    return true;
            }
        }

        return false;
    }

    private static bool TryExtractMissingExtensionRequestFromMessage(
        Diagnostic diagnostic,
        out MissingExtensionRequest request)
    {
        request = null!;

        var match = _missingMemberDiagnosticPattern.Match(diagnostic.GetMessage());
        if (!match.Success)
        {
            return false;
        }

        var receiverDisplay = NormalizeTypeName(match.Groups[1].Value);
        var methodName = match.Groups[2].Value;
        if (string.IsNullOrWhiteSpace(receiverDisplay) || string.IsNullOrWhiteSpace(methodName))
        {
            return false;
        }

        var receiverNames = new HashSet<string>(StringComparer.Ordinal) { receiverDisplay };
        var simpleReceiver = receiverDisplay;
        var genericStart = simpleReceiver.IndexOf('<', StringComparison.Ordinal);
        if (genericStart > 0)
        {
            simpleReceiver = simpleReceiver[..genericStart];
        }

        var lastDot = simpleReceiver.LastIndexOf('.');
        if (lastDot >= 0)
        {
            simpleReceiver = simpleReceiver[(lastDot + 1)..];
        }

        if (!string.IsNullOrWhiteSpace(simpleReceiver))
        {
            receiverNames.Add(simpleReceiver);
        }

        request = new MissingExtensionRequest(methodName, receiverNames, InvocationArgumentCount: null);
        return true;
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

    private static bool IsExtensionLikeStaticMethod(MethodInfo method, Type declaringType)
    {
        if (!method.IsStatic)
            return false;

        if (method.IsDefined(typeof(ExtensionAttribute), inherit: false))
            return true;

        if (method.GetParameters().Length == 0)
            return false;

        // Projectables and other generators may emit extension-like static methods without [Extension].
        return declaringType.IsAbstract
               && declaringType.IsSealed
               && declaringType.Name.EndsWith("Extensions", StringComparison.Ordinal);
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

        foreach (var candidate in EnumerateTypeHierarchy(parameterType))
        {
            if (MatchesReceiverTypeName(candidate, receiverTypeNames))
                return true;
        }

        return false;
    }

    private static bool MatchesReceiverTypeName(Type parameterType, IReadOnlySet<string> receiverTypeNames)
    {
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

    private static IEnumerable<Type> EnumerateTypeHierarchy(Type type)
    {
        var visited = new HashSet<Type>();

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (!visited.Add(current))
                break;

            yield return current;

            if (current.IsGenericType && !current.IsGenericTypeDefinition)
                yield return current.GetGenericTypeDefinition();
        }

        foreach (var iface in type.GetInterfaces())
        {
            if (!visited.Add(iface))
                continue;

            yield return iface;

            if (iface.IsGenericType && !iface.IsGenericTypeDefinition)
                yield return iface.GetGenericTypeDefinition();
        }
    }

    private static string? TryExtractRootIdentifier(string expression)
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

    private static bool TryNormalizeRootContextHopFromErrors(
        IReadOnlyList<Diagnostic> errors,
        CSharpCompilation compilation,
        string expression,
        Type dbContextType,
        out string normalizedExpression)
    {
        normalizedExpression = expression;

        var rootId = TryExtractRootIdentifier(expression);
        if (string.IsNullOrWhiteSpace(rootId))
            return false;

        if (!TryExtractLeadingHop(expression, rootId, out var hopName, out var nextMember))
            return false;

        // If DbContext already has this hop, it is not a wrapper hop.
        if (dbContextType.GetProperty(hopName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is not null)
            return false;

        // If the member after the hop is not on DbContext, removing the hop is likely incorrect.
        if (dbContextType.GetProperty(nextMember, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is null)
            return false;

        var hasMatchingError = HasMatchingRootHopError(
            errors,
            compilation,
            rootId,
            hopName,
            dbContextType);

        if (!hasMatchingError)
            return false;

        var pattern = $@"(?<!\w){Regex.Escape(rootId)}\s*\.\s*{Regex.Escape(hopName)}\s*\.";
        normalizedExpression = Regex.Replace(expression, pattern, rootId + ".");
        return !string.Equals(normalizedExpression, expression, StringComparison.Ordinal);
    }

    private static bool HasMatchingRootHopError(
        IReadOnlyList<Diagnostic> errors,
        CSharpCompilation compilation,
        string rootId,
        string hopName,
        Type dbContextType)
    {
        foreach (var diagnostic in errors.Where(d => d.Id == "CS1061"))
        {
            if (!diagnostic.Location.IsInSource || diagnostic.Location.SourceTree is null)
                continue;

            var tree = diagnostic.Location.SourceTree;
            var root = tree.GetRoot();
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

            var memberAccess = node as MemberAccessExpressionSyntax
                ?? node.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault();
            if (memberAccess is null)
                continue;

            if (!string.Equals(memberAccess.Name.Identifier.ValueText, hopName, StringComparison.Ordinal))
                continue;

            if (!TryGetLeftmostIdentifier(memberAccess.Expression, out var leftmostIdentifier)
                || !string.Equals(leftmostIdentifier, rootId, StringComparison.Ordinal))
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type
                ?? semanticModel.GetTypeInfo(memberAccess.Expression).ConvertedType;
            if (receiverType is null)
                continue;

            if (IsTypeSymbolMatch(receiverType, dbContextType))
                return true;
        }

        return false;
    }

    private static bool TryGetLeftmostIdentifier(ExpressionSyntax expression, out string identifier)
    {
        var current = expression;

        while (true)
        {
            switch (current)
            {
                case IdentifierNameSyntax id:
                    identifier = id.Identifier.ValueText;
                    return true;

                case MemberAccessExpressionSyntax member:
                    current = member.Expression;
                    continue;

                case InvocationExpressionSyntax invocation
                    when invocation.Expression is MemberAccessExpressionSyntax memberAccess:
                    current = memberAccess.Expression;
                    continue;

                case ParenthesizedExpressionSyntax parenthesized:
                    current = parenthesized.Expression;
                    continue;

                case CastExpressionSyntax cast:
                    current = cast.Expression;
                    continue;

                default:
                    identifier = string.Empty;
                    return false;
            }
        }
    }

    private static bool IsTypeSymbolMatch(ITypeSymbol typeSymbol, Type runtimeType)
    {
        if (string.Equals(typeSymbol.Name, runtimeType.Name, StringComparison.Ordinal))
            return true;

        var symbolFullName = typeSymbol
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        var runtimeFullName = (runtimeType.FullName ?? string.Empty)
            .Replace('+', '.')
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        return string.Equals(symbolFullName, runtimeFullName, StringComparison.Ordinal);
    }

    private static bool TryExtractLeadingHop(
        string expression,
        string rootId,
        out string hopName,
        out string nextMember)
    {
        hopName = string.Empty;
        nextMember = string.Empty;

        ExpressionSyntax parsed;
        try
        {
            parsed = SyntaxFactory.ParseExpression(expression);
        }
        catch
        {
            return false;
        }

        var members = new List<string>();
        var current = parsed;

        while (true)
        {
            switch (current)
            {
                case InvocationExpressionSyntax invocation
                    when invocation.Expression is MemberAccessExpressionSyntax memberAccess:
                    members.Add(memberAccess.Name.Identifier.ValueText);
                    current = memberAccess.Expression;
                    continue;

                case MemberAccessExpressionSyntax member:
                    members.Add(member.Name.Identifier.ValueText);
                    current = member.Expression;
                    continue;

                case ParenthesizedExpressionSyntax parenthesized:
                    current = parenthesized.Expression;
                    continue;

                case CastExpressionSyntax cast:
                    current = cast.Expression;
                    continue;

                case IdentifierNameSyntax identifier:
                    if (!string.Equals(identifier.Identifier.ValueText, rootId, StringComparison.Ordinal)
                        || members.Count < 2)
                    {
                        return false;
                    }

                    // members were collected from right-to-left; reverse lookup for root->... order.
                    hopName = members[^1];
                    nextMember = members[^2];
                    return true;

                default:
                    return false;
            }
        }
    }

    private static bool IsUnsupportedTopLevelMethodInvocation(string expression, string ctxVar)
    {
        var m = Regex.Match(expression,
            @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)\s*\(");
        if (!m.Success)
            return false;

        if (string.Equals(m.Groups[1].Value, ctxVar, StringComparison.Ordinal)
            && string.Equals(m.Groups[2].Value, "Set", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool ContainsFindInvocation(string expression) =>
        Regex.IsMatch(expression, @"\.\s*Find(Async)?\s*\(", RegexOptions.IgnoreCase);

}
