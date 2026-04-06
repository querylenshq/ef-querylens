// Factory-root receiver substitution for LINQ chains in query preview.
// Detects patterns like: await _contextFactory.CreateDbContextAsync(ct) rooted queries
// Substitutes factory receiver with the context variable to enable proper expression capture.
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFQueryLens.Lsp.Parsing;

public static partial class LspSyntaxHelper
{
    /// <summary>
    /// Attempts to detect a factory-root pattern and substitute the receiver with the
    /// context variable name. For example:
    ///   await _contextFactory.CreateDbContextAsync(ct).DbSet&lt;User&gt;()...
    /// becomes:
    ///   __qlContextForFactoryRoot.DbSet&lt;User&gt;()...
    ///
    /// Returns (rewritten expression, substitutionApplied, inferredContextTypeName) tuple.
    /// If no factory pattern is detected, returns the original expression with substitutionApplied=false.
    /// If substitution is applied but the inferred type is ambiguous (multiple factory candidates),
    /// returns the original expression with substitutionApplied=false to avoid semantic drift.
    /// </summary>
    internal static (string RewrittenExpression, bool SubstitutionApplied, string? FactoryContextType)
        TrySubstituteFactoryRoot(
            string expression,
            string contextVariableName,
            IReadOnlyList<string>? factoryCandidateTypeNames,
            Action<string>? debugLog = null)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return (expression, false, null);

        try
        {
            var parsed = SyntaxFactory.ParseExpression(expression);

            // Attempt to detect factory-root pattern in the parsed expression.
            var (factoryReceiver, isAsync, tContextType) = TryExtractFactoryRootPattern(
                parsed,
                factoryCandidateTypeNames,
                debugLog);

            if (factoryReceiver is null)
            {
                debugLog?.Invoke("factory-root-substitution skipped reason=no-factory-pattern-detected");
                return (expression, false, null);
            }

            // Ambiguity gate: if multiple factory candidates exist, skip substitution to avoid
            // semantic drift due to type confusion.
            if (factoryCandidateTypeNames?.Count > 1 && string.IsNullOrWhiteSpace(tContextType))
            {
                debugLog?.Invoke(
                    $"factory-root-substitution skipped reason=ambiguous-factory-candidates count={factoryCandidateTypeNames.Count}");
                return (expression, false, null);
            }

            // Determine the replacement receiver name.
            // Use a synthetic variable name to avoid conflicts with user code.
            const string replacementReceiverName = "__qlFactoryContext";

            // Walk the parsed expression and replace the factory receiver with the context variable.
            var rewriter = new FactoryRootRewriter(factoryReceiver, replacementReceiverName);
            var rewritten = rewriter.Visit(parsed);

            if (ReferenceEquals(rewritten, parsed) || rewritten is null)
            {
                debugLog?.Invoke("factory-root-substitution skipped reason=rewriter-produced-no-change");
                return (expression, false, null);
            }

            if (rewritten is not ExpressionSyntax rewrittenExpr)
            {
                debugLog?.Invoke("factory-root-substitution skipped reason=rewriter-result-not-expression");
                return (expression, false, null);
            }

            var rewrittenText = rewrittenExpr.WithoutTrivia().NormalizeWhitespace().ToString();
            // Infer contextType from the single factory candidate when the syntax parser
            // couldn't extract it directly (which is always, since we don't use a semantic model).
            var resolvedContextType = tContextType;
            if (string.IsNullOrWhiteSpace(resolvedContextType) && factoryCandidateTypeNames?.Count == 1)
                resolvedContextType = factoryCandidateTypeNames[0];

            debugLog?.Invoke(
                $"factory-root-substitution applied receiverType={resolvedContextType ?? "unknown"} isAsync={isAsync}");

            return (rewrittenText, true, resolvedContextType);
        }
        catch (Exception ex)
        {
            debugLog?.Invoke($"factory-root-substitution error={ex.GetType().Name}:{ex.Message}");
            return (expression, false, null);
        }
    }

    /// <summary>
    /// Detects a factory-root pattern in a parsed expression.
    /// Patterns:
    ///   - Async: await _contextFactory.CreateDbContextAsync(...)
    ///   - Sync: _contextFactory.CreateDbContext(...)
    ///
    /// Returns a tuple (factoryReceiver, isAsync, inferredContextType).
    /// factoryReceiver is the entire factory invocation syntax to be replaced.
    /// If no pattern is detected, returns (null, *, null).
    /// </summary>
    private static (InvocationExpressionSyntax? FactoryReceiver, bool IsAsync, string? ContextType)
        TryExtractFactoryRootPattern(
            ExpressionSyntax expression,
            IReadOnlyList<string>? factoryCandidateTypeNames,
            Action<string>? debugLog)
    {
        // Walk down the entire fluent chain to find the root expression.
        // Real chains alternate: Invocation → MemberAccess → Invocation → MemberAccess...
        // e.g. (await factory.Xyz(ct)).Set<T>().Where(...).OrderBy(...).ToListAsync(ct)
        //
        // A single-type while loop exits as soon as the pattern switches, so we need one
        // combined loop that keeps walking regardless of which alternating type is next.
        // IMPORTANT: stop when we find a factory InvocationExpression (sync pattern) rather
        // than stepping into it and walking right past it.
        ExpressionSyntax current = expression;
        
        while (true)
        {
            if (current is InvocationExpressionSyntax invocation)
            {
                // Stop if this invocation IS the factory call (sync pattern).
                if (TryIdentifyFactoryCallPattern(invocation, out _))
                    break;
                current = invocation.Expression;
            }
            else if (current is MemberAccessExpressionSyntax memberAccess)
                current = memberAccess.Expression;
            else if (current is ParenthesizedExpressionSyntax paren)
                current = paren.Expression;
            else
                break;
        }
        
        // current is now the root: AwaitExpression or factory InvocationExpression.
        // Pattern 1: await factory.CreateDbContextAsync(...)
        if (current is AwaitExpressionSyntax awaitExpr)
        {
            if (awaitExpr.Expression is InvocationExpressionSyntax factoryCall
                && TryIdentifyFactoryCallPattern(factoryCall, out var contextTypeName))
            {
                debugLog?.Invoke(
                    $"factory-root-detected pattern=await-factory-invoke contextType={contextTypeName}");
                return (factoryCall, true, contextTypeName);
            }
        }
        
        // Pattern 2: factory.CreateDbContext(...)
        if (current is InvocationExpressionSyntax syncFactoryCall
            && TryIdentifyFactoryCallPattern(syncFactoryCall, out var syncContextType))
        {
            debugLog?.Invoke(
                $"factory-root-detected pattern=sync-factory-invoke contextType={syncContextType}");
            return (syncFactoryCall, false, syncContextType);
        }

        debugLog?.Invoke("factory-root-pattern-not-matched");
        return (null, false, null);
    }

    /// <summary>
    /// Identifies if an invocation matches a known factory call pattern:
    ///   - _factory.CreateDbContextAsync(ct)   (async)
    ///   - factory.CreateDbContext(args)       (sync)
    ///   - service.GetContext(ct)              (custom factory method, if inferred from context)
    ///
    /// Returns true if recognized, with the inferred context type name if available.
    /// </summary>
    private static bool TryIdentifyFactoryCallPattern(
        InvocationExpressionSyntax invocation,
        out string? contextTypeName)
    {
        contextTypeName = null;

        // Extract the method name being called.
        string? methodName = null;
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            methodName = memberAccess.Name.Identifier.ValueText;
        }
        else if (invocation.Expression is IdentifierNameSyntax identifier)
        {
            methodName = identifier.Identifier.ValueText;
        }

        if (string.IsNullOrWhiteSpace(methodName))
            return false;

        // Recognize standard EF Core factory method names.
        var isFactoryMethod = methodName switch
        {
            "CreateDbContextAsync" => true,
            "CreateDbContext" => true,
            "GetContext" => true,           // Custom factory method pattern
            "GetContextAsync" => true,      // Custom async factory pattern
            _ => false,
        };

        return isFactoryMethod;
    }

    /// <summary>
    /// Rewrites a parsed expression by replacing the factory invocation receiver with
    /// a synthetic variable name. Checks BEFORE visiting children (pre-order) so the
    /// natural bottom-up Roslyn tree reconstruction receives the substituted receiver at
    /// every parent node without requiring a short-circuit override.
    /// </summary>
    private sealed class FactoryRootRewriter : CSharpSyntaxRewriter
    {
        private readonly InvocationExpressionSyntax _originalReceiver;
        private readonly string _replacementReceiverName;
        private bool _replaced;

        public FactoryRootRewriter(InvocationExpressionSyntax originalReceiver, string replacementReceiverName)
        {
            _originalReceiver = originalReceiver;
            _replacementReceiverName = replacementReceiverName;
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (!_replaced)
            {
                // Strip any parentheses wrapping the receiver before checking.
                // (await factory.Xyz(ct)).Member → receiver after strip = await factory.Xyz(ct)
                var receiver = node.Expression;
                while (receiver is ParenthesizedExpressionSyntax paren)
                    receiver = paren.Expression;

                // Async pattern: (await factory.CreateDbContextAsync(ct)).Member
                if (receiver is AwaitExpressionSyntax awaitExpr
                    && awaitExpr.Expression is InvocationExpressionSyntax asyncFactory
                    && SyntaxFactory.AreEquivalent(asyncFactory, _originalReceiver))
                {
                    _replaced = true;
                    return node.WithExpression(SyntaxFactory.IdentifierName(_replacementReceiverName));
                }

                // Sync pattern: factory.CreateDbContext().Member
                if (receiver is InvocationExpressionSyntax syncFactory
                    && SyntaxFactory.AreEquivalent(syncFactory, _originalReceiver))
                {
                    _replaced = true;
                    return node.WithExpression(SyntaxFactory.IdentifierName(_replacementReceiverName));
                }
            }

            return base.VisitMemberAccessExpression(node);
        }

        public override SyntaxNode? VisitAwaitExpression(AwaitExpressionSyntax node)
        {
            // Handles the standalone case: await factory.CreateDbContextAsync(ct)
            // (not followed by .Member access — e.g. the whole expression is just the await).
            if (!_replaced
                && node.Expression is InvocationExpressionSyntax factoryCall
                && SyntaxFactory.AreEquivalent(factoryCall, _originalReceiver))
            {
                _replaced = true;
                return SyntaxFactory.IdentifierName(_replacementReceiverName);
            }

            return base.VisitAwaitExpression(node);
        }
    }
}
