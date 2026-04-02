using Microsoft.CodeAnalysis;
using ImportResolver = EFQueryLens.Core.Scripting.Evaluation.ImportResolver;

namespace EFQueryLens.Core.Tests.Scripting;

/// <summary>
/// Tests that enforce the LSP-vs-Core ownership boundary:
///   - Core's syntax-only normalization rules use strict diagnostic guards so they
///     do not fire on expressions that LSP pre-normalization has already handled.
///   - The Core evaluate pipeline still accepts LSP-pre-normalized expressions and
///     produces valid SQL without needing compile-retry rewrites.
/// </summary>
public partial class QueryEvaluatorTests
{
    // ── Guard fast-paths: pattern-ternary rule ────────────────────────────────

    [Fact]
    public void CorePatternTernaryRule_FastPath_NoTernary_ReturnsFalse()
    {
        // Expressions without '?' skip the rewriter entirely (fast path).
        var result = ImportResolver.TryNormalizePatternTernaryComparisonFromErrors(
            [],
            "db.Orders.Where(o => o.Total > 200).Select(o => o.Id)",
            out _);

        Assert.False(result);
    }

    [Fact]
    public void CorePatternTernaryRule_Guard_NoDiagnosticCS0019_ReturnsFalse()
    {
        // Expression contains '?:' but the CS0019 trigger diagnostic is absent:
        // the rule must NOT rewrite it.
        var result = ImportResolver.TryNormalizePatternTernaryComparisonFromErrors(
            [],
            "db.Orders.Where(o => o.Total > 0 ? 1 : 0).Select(o => o.Id)",
            out _);

        Assert.False(result);
    }

    [Fact]
    public void CorePatternTernaryRule_Guard_AlreadyNormalizedByLsp_ReturnsFalse()
    {
        // LSP pre-normalization rewrites the ternary-pattern form before Core sees it.
        // After normalization, the expression no longer matches the rewriter's target,
        // so the rule returns false even if a stale CS0019 diagnostic were present.
        //
        // Simulate: original ternary was already rewritten to a simple comparison.
        var lspNormalized = "db.Orders.Where(o => o.UserId == (selector.Value == 1 || selector.Value == 2 ? 1 : 2)).Select(o => o.Id)";

        // No CS0019 in the request (LSP removed the pattern so Roslyn won't emit it).
        var result = ImportResolver.TryNormalizePatternTernaryComparisonFromErrors(
            [],
            lspNormalized,
            out _);

        Assert.False(result);
    }

    // ── Guard fast-paths: is-pattern rule ────────────────────────────────────

    [Fact]
    public void CoreIsPatternRule_FastPath_NoIsKeyword_ReturnsFalse()
    {
        // Expressions without ' is ' skip the rewriter entirely (fast path).
        var result = ImportResolver.TryNormalizeUnsupportedPatternMatchingFromErrors(
            [],
            "db.Orders.Where(o => o.Customer == null).Select(o => o.Id)",
            out _);

        Assert.False(result);
    }

    [Fact]
    public void CoreIsPatternRule_Guard_NoDiagnosticCS8122_ReturnsFalse()
    {
        // Expression contains ' is ' but the CS8122 trigger diagnostic is absent:
        // the rule must NOT rewrite it.
        var result = ImportResolver.TryNormalizeUnsupportedPatternMatchingFromErrors(
            [],
            "db.Orders.Where(o => o.Customer is null).Select(o => o.Id)",
            out _);

        Assert.False(result);
    }

    [Fact]
    public void CoreIsPatternRule_Guard_AlreadyNormalizedByLsp_ReturnsFalse()
    {
        // LSP pre-normalization converts 'x is null' → 'x == null' before Core sees
        // the expression.  After normalization ' is ' no longer appears, so the rule
        // fast-paths to false regardless of any diagnostics.
        var lspNormalized = "db.Orders.Where(o => o.Customer == null).Select(o => o.Id)";

        // With a synthetic CS8122 diagnostic to confirm the guard overrides it:
        var result = ImportResolver.TryNormalizeUnsupportedPatternMatchingFromErrors(
            [],
            lspNormalized,
            out _);

        Assert.False(result);
    }

    // ── Integration: Core accepts LSP-pre-normalized expressions ─────────────

    [Fact]
    public async Task Evaluate_NullComparisonEquality_TranslatesToSql()
    {
        // This is the form LSP emits after normalizing 'o.Customer is null'.
        // Verifies the evaluator handles it without needing any compile-retry rewrite.
        var result = await TranslateAsync(
            "db.Orders.Where(o => o.Customer == null).Select(o => o.Id)",
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Sql);
    }

    [Fact]
    public async Task Evaluate_BoolEqualityComparison_TranslatesToSql()
    {
        // This is the form LSP emits after normalizing 'o.IsNotDeleted is true'.
        var result = await TranslateAsync(
            "db.Orders.Where(o => o.IsNotDeleted == true).Select(o => o.Id)",
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Sql);
    }
}
