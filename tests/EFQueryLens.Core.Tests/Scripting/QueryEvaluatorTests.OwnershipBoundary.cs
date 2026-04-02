using EFQueryLens.Core.Contracts;
using Microsoft.CodeAnalysis;

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

    [Fact]
    public async Task Evaluate_WhenDaemonRewriteFallbackDisabled_DoesNotMutateExpression()
    {
        var rawExpression = "db.Orders.Where(o => o.UserId == selector.Value is 1 or 2 ? 1 : 2).Select(o => o.Id)";

        var result = await _evaluator.EvaluateAsync(
            _alcCtx,
            new TranslationRequest
            {
                AssemblyPath = _alcCtx.AssemblyPath,
                Expression = rawExpression,
                OriginalExpression = rawExpression,
                RewrittenExpression = rawExpression,
                DbContextTypeName = DefaultMySqlDbContextType,
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Compilation error", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.ExecutedExpression);
    }

    [Fact]
    public async Task Evaluate_WhenDaemonRewriteFallbackEnabled_IsIgnoredInStrictMode()
    {
        var rawExpression = "db.Orders.Where(o => o.UserId == selector.Value is 1 or 2 ? 1 : 2).Select(o => o.Id)";

        var result = await _evaluator.EvaluateAsync(
            _alcCtx,
            new TranslationRequest
            {
                AssemblyPath = _alcCtx.AssemblyPath,
                Expression = rawExpression,
                OriginalExpression = rawExpression,
                RewrittenExpression = rawExpression,
                DbContextTypeName = DefaultMySqlDbContextType,
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("Compilation error", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.ExecutedExpression);
    }

    [Fact]
    public async Task Evaluate_WhenDaemonRewriteFallbackDisabled_AllowsOnlyCs0122ProjectionRewrite()
    {
        var expression = "db.Orders.Where(o => o.Customer.CustomerId == customerId)" +
                         ".Where(o => o.IsNotDeleted)" +
                         ".Where(o => o.Total >= 100 && o.Status != OrderStatus.Cancelled)" +
                         ".Select(o => new OrderListItemDto(o.Id, o.Customer.CustomerId, o.Total, o.Status, o.CreatedUtc))";

        var result = await _evaluator.EvaluateAsync(
            _alcCtx,
            new TranslationRequest
            {
                AssemblyPath = _alcCtx.AssemblyPath,
                Expression = expression,
                OriginalExpression = expression,
                RewrittenExpression = expression,
                DbContextTypeName = "SampleMySqlApp.Infrastructure.Persistence.MySqlAppDbContext",
                AdditionalImports =
                [
                    "SampleMySqlApp.Application.Orders",
                    "SampleMySqlApp.Domain.Enums",
                ],
                LocalSymbolGraph =
                [
                    new LocalSymbolGraphEntry
                    {
                        Name = "customerId",
                        TypeName = "System.Guid",
                        Kind = "local",
                        DeclarationOrder = 0,
                    },
                ],
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.DoesNotContain("CS0122", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.NotNull(result.ExecutedExpression);
    }
}
