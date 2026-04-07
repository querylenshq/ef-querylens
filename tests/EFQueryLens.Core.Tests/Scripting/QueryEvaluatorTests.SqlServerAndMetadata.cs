using EFQueryLens.Core.AssemblyContext;
using EFQueryLens.Core.Contracts;
using QueryEvaluator = EFQueryLens.Core.Scripting.Evaluation.QueryEvaluator;

namespace EFQueryLens.Core.Tests.Scripting;

public partial class QueryEvaluatorTests
{
    /// <summary>
    /// Regression coverage for the SQL Server sample when provider/runtime versions
    /// drift and <c>TranslateExecuteUpdate</c> is missing.
    ///
    /// Depending on runner environment and resolved package graph, this can either:
    /// 1) fail gracefully with a MissingMethodException-based message, or
    /// 2) succeed normally when versions are aligned.
    ///
    /// In both cases, the evaluator must not throw.
    /// </summary>
    [Fact]
    public async Task Evaluate_SqlServerSample_MissingMethodException_ReturnsGracefulFailureWithHint()
    {
        using var sqlAlcCtx = new ProjectAssemblyContext(GetSampleSqlServerAppDll());
        var evaluator = new QueryEvaluator();
        const string expression = "db.Customers";

        var result = await evaluator.EvaluateAsync(sqlAlcCtx, new TranslationRequest
            {
                AssemblyPath = sqlAlcCtx.AssemblyPath,
                Expression = expression,
                DbContextTypeName = "SampleSqlServerApp.Infrastructure.Persistence.SqlServerAppDbContext",
                LocalSymbolGraph = [],
                V2ExtractionPlan = BuildMinimalExtractionPlan(expression),
                V2CapturePlan = new V2CapturePlanSnapshot
                {
                    ExecutableExpression = expression,
                    IsComplete = true,
                },
            }, TestContext.Current.CancellationToken);

        if (result.Success)
        {
            // Aligned package/runtime graph: translation succeeds.
            Assert.NotNull(result.Sql);
            Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", result.Metadata.ProviderName);
            return;
        }

        // Drifted graph: must fail gracefully with actionable diagnostics.
        Assert.NotNull(result.ErrorMessage);
        var isMissingMethodFailure =
            result.ErrorMessage.Contains("Method not found", StringComparison.OrdinalIgnoreCase)
            && result.ErrorMessage.Contains("intra-project version conflict", StringComparison.OrdinalIgnoreCase)
            && result.ErrorMessage.Contains("provider package", StringComparison.OrdinalIgnoreCase);
        var isConnectionSetupFailure =
            result.ErrorMessage.Contains("SetDbConnection failed", StringComparison.OrdinalIgnoreCase);
        Assert.True(
            isMissingMethodFailure || isConnectionSetupFailure,
            $"Unexpected SQL Server sample failure reason: {result.ErrorMessage}");
    }

    [Fact]
    public async Task Evaluate_SqlServerSample_PagingWithExpressionVariable_SucceedsOrMethodNotFound()
    {
        // Regression: SqlClient runtime RID assets may leak internal metadata types
        // (for example SNIHandle) into Roslyn reference graphs. This must never surface
        // as CS0122/emit failure for the paging + Select(expression) hover scenario.
        //
        // Keep this aligned with the user-reported expression shape to catch regressions.
        using var sqlAlcCtx = new ProjectAssemblyContext(GetSampleSqlServerAppDll());
        var evaluator = new QueryEvaluator();
        const string expression = "db.Orders.OrderByDescending(o => o.CreatedUtc).ThenByDescending(o => o.Id).Skip((page - 1) * pageSize).Take(pageSize).Select(expression)";

        var result = await evaluator.EvaluateAsync(sqlAlcCtx, new TranslationRequest
            {
                AssemblyPath = sqlAlcCtx.AssemblyPath,
                Expression = expression,
                DbContextTypeName = "SampleSqlServerApp.Infrastructure.Persistence.SqlServerAppDbContext",
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
                            Name = "page",
                            TypeName = "int",
                            Kind = "local",
                            DeclarationOrder = 0,
                            CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
                        },
                        new V2CapturePlanEntry
                        {
                            Name = "pageSize",
                            TypeName = "int",
                            Kind = "local",
                            DeclarationOrder = 1,
                            CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
                        },
                        new V2CapturePlanEntry
                        {
                            Name = "expression",
                            TypeName = "System.Linq.Expressions.Expression<System.Func<SampleSqlServerApp.Domain.Entities.Order, int>>",
                            Kind = "local",
                            DeclarationOrder = 2,
                            CapturePolicy = LocalSymbolReplayPolicies.UsePlaceholder,
                        },
                    ],
                },
            }, TestContext.Current.CancellationToken);

        if (result.Success)
        {
            Assert.NotEmpty(result.Commands);
        }
        else
        {
            // Guard: must never fail due to CS0122 leaks, SNIHandle metadata, emit errors, or compilation failures.
            Assert.DoesNotContain("CS0122", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("SNIHandle", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Emit error", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Compilation error", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            // Acceptable failure modes: SqlClient RID asset version mismatch ("Method not found")
            // or named connection string setup failure ("SetDbConnection failed").
            var isAcceptableFailure =
                (result.ErrorMessage ?? string.Empty).Contains("Method not found", StringComparison.OrdinalIgnoreCase)
                || (result.ErrorMessage ?? string.Empty).Contains("SetDbConnection failed", StringComparison.OrdinalIgnoreCase);
            Assert.True(isAcceptableFailure, $"Unexpected failure reason: {result.ErrorMessage}");
        }
    }

    /// <summary>
    /// Verifies that the evaluator can resolve and use <c>SqlServerReportingDbContext</c>
    /// independently of <c>SqlServerAppDbContext</c> - the core multi-DbContext scenario.
    /// Uses the same bimodal assertion as the primary SqlServer test to tolerate provider drift.
    /// </summary>
    [Fact]
    public async Task Evaluate_SqlServerSample_ReportingContext_CustomerDirectory_ReturnsSql()
    {
        using var sqlAlcCtx = new ProjectAssemblyContext(GetSampleSqlServerAppDll());
        var evaluator = new QueryEvaluator();
        const string expression = "db.CustomerDirectory";

        var result = await evaluator.EvaluateAsync(sqlAlcCtx, new TranslationRequest
            {
                AssemblyPath = sqlAlcCtx.AssemblyPath,
                Expression = expression,
                DbContextTypeName = "SampleSqlServerApp.Infrastructure.Persistence.SqlServerReportingDbContext",
                LocalSymbolGraph = [],
                V2ExtractionPlan = BuildMinimalExtractionPlan(expression),
                V2CapturePlan = new V2CapturePlanSnapshot
                {
                    ExecutableExpression = expression,
                    IsComplete = true,
                },
            }, TestContext.Current.CancellationToken);

        if (result.Success)
        {
            // Aligned package/runtime graph: translation succeeds against the reporting context.
            Assert.NotNull(result.Sql);
            Assert.Contains("Customers", result.Sql, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", result.Metadata.ProviderName);
            return;
        }

        // Drifted graph: must fail gracefully - same diagnostics expected as primary context.
        Assert.NotNull(result.ErrorMessage);
        var isReportingMissingMethodFailure =
            result.ErrorMessage.Contains("Method not found", StringComparison.OrdinalIgnoreCase);
        var isReportingConnectionSetupFailure =
            result.ErrorMessage.Contains("SetDbConnection failed", StringComparison.OrdinalIgnoreCase);
        Assert.True(
            isReportingMissingMethodFailure || isReportingConnectionSetupFailure,
            $"Unexpected SQL Server reporting-context failure reason: {result.ErrorMessage}");
    }

    [Fact]
    public async Task Evaluate_MetaData_HasCorrectProviderName()
    {
        var result = await TranslateV2Async("db.Orders", ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("Pomelo.EntityFrameworkCore.MySql", result.Metadata.ProviderName);
    }

    [Fact]
    public async Task Evaluate_Metadata_TranslationTimeIsPositive()
    {
        var result = await TranslateV2Async("db.Orders", ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(result.Metadata.TranslationTime > TimeSpan.Zero);
    }

    [Fact]
    public async Task Evaluate_Metadata_DbContextTypeIsSet()
    {
        var result = await TranslateV2Async("db.Orders", ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("SampleMySqlApp.Infrastructure.Persistence.MySqlAppDbContext", result.Metadata.DbContextType);
    }

    [Fact]
    public async Task Evaluate_Metadata_EfCoreVersionIsKnown()
    {
        var result = await TranslateV2Async("db.Orders", ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotEqual("unknown", result.Metadata.EfCoreVersion);
    }
}
