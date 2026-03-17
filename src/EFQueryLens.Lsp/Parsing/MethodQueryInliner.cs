using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

public static partial class MethodQueryInliner
{
    public sealed record InlineSelectorDiagnostics
    {
        public bool SelectorParameterDetected { get; init; }
        public bool SelectorArgumentSubstituted { get; init; }
        public bool SelectorArgumentSanitized { get; init; }
        public bool ContainsNestedSelectorMaterialization { get; init; }
    }

    private sealed record ParameterMapBuildResult(
        Dictionary<string, ExpressionSyntax> Map,
        bool SelectorParameterDetected,
        bool SelectorArgumentSubstituted,
        bool SelectorArgumentSanitized,
        bool ContainsNestedSelectorMaterialization);

    private static readonly HashSet<string> TerminalMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "ToList", "ToListAsync", "ToArray", "ToArrayAsync", "ToDictionary", "ToDictionaryAsync",
        "First", "FirstOrDefault", "FirstAsync", "FirstOrDefaultAsync",
        "Single", "SingleOrDefault", "SingleAsync", "SingleOrDefaultAsync",
        "Last", "LastOrDefault", "LastAsync", "LastOrDefaultAsync",
        "Count", "CountAsync", "LongCount", "LongCountAsync",
        "Any", "AnyAsync", "All", "AllAsync",
        "Min", "MinAsync", "Max", "MaxAsync", "Sum", "SumAsync", "Average", "AverageAsync",
        "ElementAt", "ElementAtOrDefault", "ElementAtAsync", "ElementAtOrDefaultAsync",
        "AsEnumerable", "AsAsyncEnumerable", "ToLookup", "ToLookupAsync",
        "ExecuteUpdate", "ExecuteUpdateAsync", "ExecuteDelete", "ExecuteDeleteAsync"
    };

    private static readonly HashSet<string> NestedMaterializationMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "ToList", "ToListAsync", "ToArray", "ToArrayAsync", "ToDictionary", "ToDictionaryAsync", "ToLookup", "ToLookupAsync"
    };

    public static bool TryInlineTopLevelInvocation(
        string sourceText,
        string sourceFilePath,
        string expression,
        bool substituteSelectorArguments,
        out string inlinedExpression,
        out string? contextVariableName,
        out string? reason)
    {
        return TryInlineTopLevelInvocation(
            sourceText,
            sourceFilePath,
            expression,
            substituteSelectorArguments,
            out inlinedExpression,
            out contextVariableName,
            out _,
            out _,
            out reason);
    }

    public static bool TryInlineTopLevelInvocation(
        string sourceText,
        string sourceFilePath,
        string expression,
        bool substituteSelectorArguments,
        out string inlinedExpression,
        out string? contextVariableName,
        out string? selectedMethodSourcePath,
        out string? reason)
    {
        return TryInlineTopLevelInvocation(
            sourceText,
            sourceFilePath,
            expression,
            substituteSelectorArguments,
            out inlinedExpression,
            out contextVariableName,
            out selectedMethodSourcePath,
            out _,
            out reason);
    }

    public static bool TryInlineTopLevelInvocation(
        string sourceText,
        string sourceFilePath,
        string expression,
        bool substituteSelectorArguments,
        out string inlinedExpression,
        out string? contextVariableName,
        out string? selectedMethodSourcePath,
        out InlineSelectorDiagnostics diagnostics,
        out string? reason)
    {
        inlinedExpression = expression;
        contextVariableName = null;
        selectedMethodSourcePath = null;
        diagnostics = new InlineSelectorDiagnostics();
        reason = null;

        if (string.IsNullOrWhiteSpace(expression))
        {
            reason = "Expression was empty.";
            return false;
        }

        if (!TryParseTopLevelInvocation(
                expression,
                out var parsedExpression,
                out var topInvocation,
                out var rootName,
                out var methodName))
        {
            reason = "Expression was not a top-level member invocation.";
            return false;
        }

        // Avoid work for already-query-shaped roots.
        if (string.Equals(rootName, "db", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rootName, "context", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rootName, "dbContext", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Invocation root already looks like a DbContext variable.";
            return false;
        }

        var searchRoot = FindSearchRoot(sourceFilePath);
        if (searchRoot == null)
        {
            reason = "Could not determine source search root.";
            return false;
        }

        var argumentExpressions = topInvocation.ArgumentList.Arguments
            .Select(a => a.Expression)
            .ToArray();

        var candidates = FindMethodCandidates(searchRoot, sourceFilePath, sourceText, methodName, argumentExpressions.Length)
            .ToList();

        if (candidates.Count == 0)
        {
            reason = "No method candidates were found for invocation.";
            return false;
        }

        var best = candidates
            .Select(c => new { Candidate = c, Score = ScoreCandidate(c.Method, argumentExpressions) })
            .OrderByDescending(x => x.Score)
            .First();

        if (best.Score < 0)
        {
            reason = "No method candidates matched invocation arguments.";
            return false;
        }

        var method = best.Candidate.Method;
        var calleeQuery = TryExtractReturnedQueryExpression(method);
        if (calleeQuery == null)
        {
            reason = "Candidate method did not expose a supported query return shape.";
            return false;
        }

        var mapResult = BuildParameterArgumentMap(method, argumentExpressions, substituteSelectorArguments);
        var substituted = (ExpressionSyntax)new ParameterSubstitutionRewriter(mapResult.Map).Visit(calleeQuery)!;
        var stripped = StripTrailingTerminalMethods(substituted);
        var rewrittenExpression = ReplaceInvocationInExpression(parsedExpression, topInvocation, stripped);
        var normalizedExpression = StripTrailingTerminalMethods(rewrittenExpression);

        diagnostics = new InlineSelectorDiagnostics
        {
            SelectorParameterDetected = mapResult.SelectorParameterDetected,
            SelectorArgumentSubstituted = mapResult.SelectorArgumentSubstituted,
            SelectorArgumentSanitized = mapResult.SelectorArgumentSanitized,
            ContainsNestedSelectorMaterialization = mapResult.ContainsNestedSelectorMaterialization,
        };

        var extractedRoot = TryExtractRootContextVariable(normalizedExpression);
        if (string.IsNullOrWhiteSpace(extractedRoot))
        {
            reason = "Inlined expression root could not be determined.";
            return false;
        }

        inlinedExpression = normalizedExpression.NormalizeWhitespace().ToString();
        contextVariableName = extractedRoot;
        selectedMethodSourcePath = best.Candidate.FilePath;
        return true;
    }

}