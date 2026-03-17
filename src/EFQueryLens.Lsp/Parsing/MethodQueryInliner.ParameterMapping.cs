using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

public static partial class MethodQueryInliner
{
    private static ParameterMapBuildResult BuildParameterArgumentMap(
        MethodDeclarationSyntax method,
        IReadOnlyList<ExpressionSyntax> arguments,
        bool substituteSelectorArguments)
    {
        var map = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        var parameters = method.ParameterList.Parameters;
        var selectorParameterDetected = false;
        var selectorArgumentSubstituted = false;
        var selectorArgumentSanitized = false;
        var containsNestedSelectorMaterialization = false;

        for (var i = 0; i < arguments.Count && i < parameters.Count; i++)
        {
            var name = parameters[i].Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var argument = arguments[i];
            var parameter = parameters[i];
            var isSelectorParameter = IsSelectorParameter(parameter, argument);

            if (isSelectorParameter)
            {
                selectorParameterDetected = true;
                if (ContainsNestedMaterialization(argument))
                {
                    containsNestedSelectorMaterialization = true;
                }
            }

            if (ShouldSkipSubstitution(parameter, argument, substituteSelectorArguments))
            {
                continue;
            }

            if (substituteSelectorArguments)
            {
                var originalArgument = argument;
                argument = SanitizeSelectorArgument(parameter, argument);
                if (!ReferenceEquals(originalArgument, argument))
                {
                    selectorArgumentSanitized = true;
                }
            }

            if (isSelectorParameter)
            {
                selectorArgumentSubstituted = true;
            }

            map[name] = argument;
        }

        return new ParameterMapBuildResult(
            map,
            selectorParameterDetected,
            selectorArgumentSubstituted,
            selectorArgumentSanitized,
            containsNestedSelectorMaterialization);
    }

    private static bool IsSelectorParameter(ParameterSyntax parameter, ExpressionSyntax argument)
    {
        var parameterType = parameter.Type?.ToString() ?? string.Empty;
        return parameterType.Contains("Expression", StringComparison.Ordinal)
               && (argument is LambdaExpressionSyntax || argument is AnonymousMethodExpressionSyntax);
    }

    private static bool ContainsNestedMaterialization(ExpressionSyntax argument)
    {
        if (argument is not LambdaExpressionSyntax && argument is not AnonymousMethodExpressionSyntax)
        {
            return false;
        }

        foreach (var invocation in argument.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && NestedMaterializationMethods.Contains(memberAccess.Name.Identifier.ValueText))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldSkipSubstitution(
        ParameterSyntax parameter,
        ExpressionSyntax argument,
        bool substituteSelectorArguments)
    {
        var parameterType = parameter.Type?.ToString() ?? string.Empty;

        // In conservative mode keep selector parameters as identifiers so QueryEvaluator
        // can synthesize a safe typed placeholder when endpoint DTO types are unavailable.
        if (parameterType.Contains("Expression", StringComparison.Ordinal) &&
            (argument is LambdaExpressionSyntax || argument is AnonymousMethodExpressionSyntax))
        {
            return !substituteSelectorArguments;
        }

        // Keep member-access arguments (for example req.WorkflowType) as method
        // parameters to avoid introducing unresolved request DTO roots.
        if (argument is MemberAccessExpressionSyntax)
        {
            return true;
        }

        return false;
    }

    private static ExpressionSyntax SanitizeSelectorArgument(ParameterSyntax parameter, ExpressionSyntax argument)
    {
        var parameterType = parameter.Type?.ToString() ?? string.Empty;
        if (!parameterType.Contains("Expression", StringComparison.Ordinal))
        {
            return argument;
        }

        if (argument is not LambdaExpressionSyntax && argument is not AnonymousMethodExpressionSyntax)
        {
            return argument;
        }

        var rewritten = new ProjectionTypeSanitizer().Visit(argument) as ExpressionSyntax;
        return rewritten ?? argument;
    }
}
