using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

/// <summary>
/// Semantic variable tracking for Phase 2 deduplication.
/// Instead of deduplicating literal text repetitions, track symbol usage
/// throughout the expression and preserve variable names when symbols are
/// reused 3+ times.
/// </summary>
public static partial class LspSyntaxHelper
{
    /// <summary>
    /// Applies semantic variable tracking to an extracted expression.
    /// Identifies reused variables and generates output with variable declarations.
    /// Only annotates non-lambda variables that are meaningfully reused.
    /// </summary>
    private static string ApplySemanticVariableTracking(
        ExpressionSyntax expression,
        string sourceText)
    {
        try
        {
            var tracker = new SemanticVariableTracker(sourceText);
            var symbolUsage = tracker.AnalyzeExpression(expression);

            if (symbolUsage.Count == 0)
            {
                return expression.ToString();
            }

            // Identify reused symbols (appear 3+ times) that are NOT lambda parameters
            // Lambda parameters (like 's' in '.Where(s => ...)') are noise - we only care about
            // reused local variables and method parameters
            var reusedSymbols = symbolUsage
                .Where(kvp => kvp.Value.Count >= 3 && !IsLambdaParameter(kvp.Key))
                .Select(kvp => kvp.Key)
                .ToHashSet(StringComparer.Ordinal);

            if (reusedSymbols.Count == 0)
            {
                return expression.ToString();
            }

            // Generate output with variable declarations
            return GenerateSemanticOutput(expression, symbolUsage, reusedSymbols);
        }
        catch
        {
            // Best-effort: if semantic tracking fails, fall back to plain expression
            return expression.ToString();
        }
    }

    /// <summary>
    /// Heuristic: detect if a symbol is likely a lambda parameter.
    /// Lambda parameters in LINQ are typically single-letter or short names like 's', 'c', 'o', 'p'.
    /// </summary>
    private static bool IsLambdaParameter(string symbolName)
    {
        // Single-letter identifiers are almost always lambda parameters
        if (symbolName.Length == 1)
        {
            return true;
        }

        // Very short lowercase identifiers are common lambda parameter names
        if (symbolName.Length <= 2 && char.IsLower(symbolName[0]))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Generates semantic output with variable usage annotations.
    /// Places variable declarations as comments at the top, followed by the expression.
    /// </summary>
    private static string GenerateSemanticOutput(
        ExpressionSyntax expression,
        Dictionary<string, List<SymbolUsage>> symbolUsage,
        HashSet<string> reusedSymbols)
    {
        var sb = new StringBuilder();
        var lines = new List<string>();

        // For each reused symbol, add a declaration comment
        foreach (var symbolName in reusedSymbols.OrderBy(s => s))
        {
            var usages = symbolUsage[symbolName];
            var usage = usages[0]; // Get first usage for type info

            if (!string.IsNullOrWhiteSpace(usage.DeclaredType))
            {
                lines.Add(
                    $"// var {symbolName}: {usage.DeclaredType} (used {usages.Count}x)");
            }
            else
            {
                lines.Add($"// var {symbolName} (used {usages.Count}x)");
            }
        }

        if (lines.Count > 0)
        {
            sb.AppendLine(string.Join(Environment.NewLine, lines));
            sb.AppendLine();
        }

        sb.Append(expression.ToString());
        return sb.ToString();
    }

    /// <summary>
    /// Tracks semantic variable usage throughout an expression.
    /// Identifies which symbols are reused (candidates for deduplication).
    /// </summary>
    private sealed class SemanticVariableTracker
    {
        private readonly string _sourceText;
        private readonly Dictionary<string, List<SymbolUsage>> _symbolUsage = new();
        private readonly HashSet<string> _declaredSymbols = new();

        public SemanticVariableTracker(string sourceText)
        {
            _sourceText = sourceText;
            CaptureLocalVariableDeclarations(sourceText);
        }

        /// <summary>
        /// Analyzes an expression and returns a map of symbol names to their usage records.
        /// </summary>
        public Dictionary<string, List<SymbolUsage>> AnalyzeExpression(
            ExpressionSyntax expression)
        {
            VisitNode(expression);
            return _symbolUsage;
        }

        /// <summary>
        /// Recursively visits all syntax nodes to identify identifier usage.
        /// </summary>
        private void VisitNode(SyntaxNode? node)
        {
            if (node == null)
            {
                return;
            }

            if (node is IdentifierNameSyntax identifier)
            {
                var name = identifier.Identifier.Text;
                if (_declaredSymbols.Contains(name))
                {
                    if (!_symbolUsage.ContainsKey(name))
                    {
                        _symbolUsage[name] = new();
                    }

                    var usageKind = DetermineUsageKind(identifier);
                    _symbolUsage[name].Add(new SymbolUsage(
                        Name: name,
                        DeclaredType: null,
                        Kind: usageKind,
                        Context: identifier.Parent?.ToString() ?? "unknown"));
                }
            }

            foreach (var child in node.ChildNodes())
            {
                VisitNode(child);
            }
        }

        /// <summary>
        /// Captures local variable and parameter declarations from the source tree.
        /// </summary>
        private void CaptureLocalVariableDeclarations(string sourceText)
        {
            try
            {
                var tree = CSharpSyntaxTree.ParseText(sourceText);
                var root = tree.GetRoot();

                // Capture local variable declarations
                foreach (var local in root
                    .DescendantNodes()
                    .OfType<LocalDeclarationStatementSyntax>())
                {
                    foreach (var variable in local.Declaration.Variables)
                    {
                        var name = variable.Identifier.Text;
                        var type = local.Declaration.Type.ToString();
                        _declaredSymbols.Add(name);

                        if (!_symbolUsage.ContainsKey(name))
                        {
                            _symbolUsage[name] = new();
                        }

                        _symbolUsage[name].Add(new SymbolUsage(
                            Name: name,
                            DeclaredType: type,
                            Kind: SymbolUsageKind.Declaration,
                            Context: $"local {type}"));
                    }
                }

                // Capture parameters
                foreach (var parameter in root
                    .DescendantNodes()
                    .OfType<ParameterSyntax>())
                {
                    var name = parameter.Identifier.Text;
                    var type = parameter.Type?.ToString() ?? "unknown";
                    _declaredSymbols.Add(name);

                    if (!_symbolUsage.ContainsKey(name))
                    {
                        _symbolUsage[name] = new();
                    }

                    _symbolUsage[name].Add(new SymbolUsage(
                        Name: name,
                        DeclaredType: type,
                        Kind: SymbolUsageKind.Declaration,
                        Context: $"parameter {type}"));
                }
            }
            catch
            {
                // Best-effort - continue without declaration info if parsing fails
            }
        }

        /// <summary>
        /// Determines the usage kind based on the parent node context.
        /// </summary>
        private static SymbolUsageKind DetermineUsageKind(IdentifierNameSyntax node)
        {
            var parent = node.Parent;

            if (parent is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Expression == node)
            {
                return SymbolUsageKind.PropertyAccess;
            }

            if (parent is AssignmentExpressionSyntax assignment &&
                assignment.Left == node)
            {
                return SymbolUsageKind.Assignment;
            }

            if (parent is ArgumentSyntax)
            {
                return SymbolUsageKind.MethodArgument;
            }

            if (parent is IsPatternExpressionSyntax or ConditionalExpressionSyntax)
            {
                return SymbolUsageKind.ConditionalGuard;
            }

            return SymbolUsageKind.Other;
        }
    }

    /// <summary>
    /// Records a single usage of a symbol.
    /// </summary>
    private sealed record SymbolUsage(
        string Name,
        string? DeclaredType,
        SymbolUsageKind Kind,
        string Context);

    /// <summary>
    /// Categorizes the context in which a symbol is used.
    /// </summary>
    private enum SymbolUsageKind
    {
        Declaration,
        PropertyAccess,
        Assignment,
        MethodArgument,
        ConditionalGuard,
        Other
    }
}
