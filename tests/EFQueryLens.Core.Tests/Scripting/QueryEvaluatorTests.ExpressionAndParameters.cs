using EFQueryLens.Core.Contracts;

namespace EFQueryLens.Core.Tests.Scripting;

public partial class QueryEvaluatorTests
{
    [Fact]
    public async Task Evaluate_WhereClauseReceivesExpressionPredicateVariable_ReturnsSql()
    {
        const string expression = "db.Orders.Where(filter).ToListAsync(ct)";

        var result = await _evaluator.EvaluateAsync(_alcCtx, new TranslationRequest
        {
            AssemblyPath = _alcCtx.AssemblyPath,
            DbContextTypeName = DefaultMySqlDbContextType,
            Expression = expression,
            LocalSymbolGraph = [],
            V2ExtractionPlan = BuildMinimalExtractionPlan(expression),
            V2CapturePlan = new V2CapturePlanSnapshot
            {
                ExecutableExpression = expression,
                IsComplete = true,
                Entries =
                [
                    new V2CapturePlanEntry
                    {
                        Name = "filter",
                        TypeName = "System.Linq.Expressions.Expression<System.Func<SampleMySqlApp.Domain.Entities.Order, bool>>",
                        Kind = "local",
                        DeclarationOrder = 0,
                        CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
                    },
                    new V2CapturePlanEntry
                    {
                        Name = "ct",
                        TypeName = "System.Threading.CancellationToken",
                        Kind = "local",
                        DeclarationOrder = 1,
                        CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
                    },
                ],
            },
        }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Sql);
        Assert.Contains("Orders", result.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Evaluate_WhereClauseReceivesShortExpressionPredicateVariable_ReturnsSql()
    {
        const string expression = "db.Orders.Where(filter).ToListAsync(ct)";

        var result = await _evaluator.EvaluateAsync(_alcCtx, new TranslationRequest
        {
            AssemblyPath = _alcCtx.AssemblyPath,
            DbContextTypeName = DefaultMySqlDbContextType,
            Expression = expression,
            AdditionalImports = ["System.Linq.Expressions"],
            LocalSymbolGraph = [],
            V2ExtractionPlan = BuildMinimalExtractionPlan(expression),
            V2CapturePlan = new V2CapturePlanSnapshot
            {
                ExecutableExpression = expression,
                IsComplete = true,
                Entries =
                [
                    new V2CapturePlanEntry
                    {
                        Name = "filter",
                        TypeName = "Expression<Func<SampleMySqlApp.Domain.Entities.Order, bool>>",
                        Kind = "local",
                        DeclarationOrder = 0,
                        CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
                    },
                    new V2CapturePlanEntry
                    {
                        Name = "ct",
                        TypeName = "System.Threading.CancellationToken",
                        Kind = "local",
                        DeclarationOrder = 1,
                        CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
                    },
                ],
            },
        }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Sql);
        Assert.Contains("Orders", result.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Evaluate_WhereWithParam_ParsesParameters()
    {
        var result = await TranslateV2Async("db.Orders.Where(o => o.UserId == 5)", ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Sql);

        if (result.Parameters.Count > 0)
        {
            var p = result.Parameters[0];
            Assert.StartsWith("@", p.Name);
            Assert.Equal("5", p.InferredValue);
        }
        else
        {
            Assert.Contains("5", result.Sql, StringComparison.Ordinal);
        }
    }
}
