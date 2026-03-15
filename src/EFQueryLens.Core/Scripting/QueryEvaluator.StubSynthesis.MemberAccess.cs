using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Core.Scripting;

public sealed partial class QueryEvaluator
{
    private static IReadOnlyDictionary<string, Type> InferMemberAccessTypes(
        string variableName,
        string expression,
        Type dbContextType,
        IReadOnlyDictionary<string, string> usingAliases)
    {
        var members = Regex.Matches(
            expression,
            $@"(?<!\w){Regex.Escape(variableName)}\.(\w+)")
            .Cast<Match>()
            // Ignore method calls like userIds.Contains(...); synthesize only property-style member access.
            .Where(m => !IsInvokedMemberAccess(m, expression))
            .Select(m => m.Groups[1].Value)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (members.Count == 0)
            return new Dictionary<string, Type>(StringComparer.Ordinal);

        var result = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var member in members)
        {
            var inferred = InferMemberTypeFromIsPattern(variableName, member, expression, dbContextType, usingAliases)
                ?? InferMemberTypeFromComparison(variableName, member, expression, dbContextType)
                ?? FindEntityPropertyType(dbContextType, member)
                ?? InferMemberTypeFromNameHeuristic(member);

            if (inferred is not null)
                result[member] = inferred;
        }

        return result;
    }

    private static Type? InferMemberTypeFromIsPattern(
        string variableName,
        string memberName,
        string expression,
        Type dbContextType,
        IReadOnlyDictionary<string, string> usingAliases)
    {
        ExpressionSyntax parsed;
        try
        {
            parsed = SyntaxFactory.ParseExpression(expression);
        }
        catch
        {
            return null;
        }

        foreach (var isPattern in parsed.DescendantNodesAndSelf().OfType<IsPatternExpressionSyntax>())
        {
            if (!IsTargetMemberAccess(isPattern.Expression, variableName, memberName))
                continue;

            var typeName = TryExtractTypeNameFromPattern(isPattern.Pattern);
            if (string.IsNullOrWhiteSpace(typeName))
                continue;

            var resolved = ResolveTypeFromName(typeName!, dbContextType, usingAliases);
            if (resolved?.IsEnum == true)
                return resolved;
        }

        return null;
    }

    private static bool IsTargetMemberAccess(ExpressionSyntax expression, string variableName, string memberName)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return IsTargetMemberAccess(parenthesized.Expression, variableName, memberName);

            case CastExpressionSyntax cast:
                return IsTargetMemberAccess(cast.Expression, variableName, memberName);

            case MemberAccessExpressionSyntax memberAccess:
                return memberAccess.Expression is IdentifierNameSyntax identifier
                       && string.Equals(identifier.Identifier.ValueText, variableName, StringComparison.Ordinal)
                       && string.Equals(memberAccess.Name.Identifier.ValueText, memberName, StringComparison.Ordinal);

            default:
                return false;
        }
    }

    private static string? TryExtractTypeNameFromPattern(PatternSyntax pattern)
    {
        switch (pattern)
        {
            case ConstantPatternSyntax constantPattern:
                return TryExtractTypeNameFromConstantPattern(constantPattern.Expression);

            case BinaryPatternSyntax binaryPattern:
                return TryExtractTypeNameFromPattern(binaryPattern.Left)
                    ?? TryExtractTypeNameFromPattern(binaryPattern.Right);

            case ParenthesizedPatternSyntax parenthesizedPattern:
                return TryExtractTypeNameFromPattern(parenthesizedPattern.Pattern);

            case DeclarationPatternSyntax declarationPattern:
                return declarationPattern.Type.ToString();

            case TypePatternSyntax typePattern:
                return typePattern.Type.ToString();

            default:
                return null;
        }
    }

    private static string? TryExtractTypeNameFromConstantPattern(ExpressionSyntax constantExpression)
    {
        switch (constantExpression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                // EnumValue pattern: Namespace.EnumType.Member -> return Namespace.EnumType
                return memberAccess.Expression.ToString();

            case ParenthesizedExpressionSyntax parenthesized:
                return TryExtractTypeNameFromConstantPattern(parenthesized.Expression);

            case CastExpressionSyntax cast:
                return cast.Type.ToString();

            default:
                return null;
        }
    }

    private static Type? ResolveTypeFromName(
        string typeName,
        Type dbContextType,
        IReadOnlyDictionary<string, string> usingAliases)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var expanded = ExpandAliasTypeName(typeName, usingAliases);
        if (string.IsNullOrWhiteSpace(expanded))
            return null;

        expanded = expanded!.Replace("global::", string.Empty, StringComparison.Ordinal);

        var alc = AssemblyLoadContext.GetLoadContext(dbContextType.Assembly);
        var assemblies = (alc?.Assemblies ?? [dbContextType.Assembly]).Distinct().ToArray();

        foreach (var asm in assemblies)
        {
            var direct = asm.GetType(expanded, throwOnError: false, ignoreCase: false);
            if (direct is not null)
                return direct;
        }

        var simpleName = expanded.Contains('.') ? expanded[(expanded.LastIndexOf('.') + 1)..] : expanded;
        foreach (var asm in assemblies)
        {
            try
            {
                var matched = asm.GetTypes().FirstOrDefault(t =>
                    string.Equals(t.Name, simpleName, StringComparison.Ordinal)
                    || string.Equals(t.FullName, expanded, StringComparison.Ordinal));
                if (matched is not null)
                    return matched;
            }
            catch (ReflectionTypeLoadException rtle)
            {
                var matched = rtle.Types
                    .Where(t => t is not null)
                    .FirstOrDefault(t =>
                        string.Equals(t!.Name, simpleName, StringComparison.Ordinal)
                        || string.Equals(t.FullName, expanded, StringComparison.Ordinal));
                if (matched is not null)
                    return matched!;
            }
            catch
            {
            }
        }

        return null;
    }

    private static string? ExpandAliasTypeName(string typeName, IReadOnlyDictionary<string, string> usingAliases)
    {
        if (string.IsNullOrWhiteSpace(typeName) || usingAliases.Count == 0)
            return typeName;

        foreach (var kvp in usingAliases)
        {
            if (string.Equals(typeName, kvp.Key, StringComparison.Ordinal))
                return kvp.Value;

            var prefix = kvp.Key + ".";
            if (typeName.StartsWith(prefix, StringComparison.Ordinal))
                return kvp.Value + typeName[prefix.Length..];
        }

        return typeName;
    }

    private static bool IsInvokedMemberAccess(Match match, string expression)
    {
        var index = match.Index + match.Length;
        while (index < expression.Length && char.IsWhiteSpace(expression[index]))
        {
            index++;
        }

        return index < expression.Length && expression[index] == '(';
    }

    private static Type? InferMemberTypeFromComparison(string variableName, string memberName, string expression, Type dbContextType)
    {
        var pattern =
            $@"\.(\w+)\s*(?:==|!=|>|<|>=|<=)\s*{Regex.Escape(variableName)}\.{Regex.Escape(memberName)}(?!\w)"
            + "|"
            + $@"(?<!\w){Regex.Escape(variableName)}\.{Regex.Escape(memberName)}\s*(?:==|!=|>|<|>=|<=)\s*\w+\.(\w+)";

        var match = Regex.Match(expression, pattern);
        if (!match.Success)
            return null;

        var entityProperty = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return FindEntityPropertyType(dbContextType, entityProperty);
    }

    private static Type? InferMemberTypeFromNameHeuristic(string memberName)
    {
        if (string.Equals(memberName, "Now", StringComparison.Ordinal)
            || string.Equals(memberName, "UtcNow", StringComparison.Ordinal)
            || string.Equals(memberName, "Today", StringComparison.Ordinal))
        {
            return typeof(DateTime);
        }

        if (memberName.EndsWith("Id", StringComparison.Ordinal))
            return typeof(Guid);

        return typeof(string);
    }
}
