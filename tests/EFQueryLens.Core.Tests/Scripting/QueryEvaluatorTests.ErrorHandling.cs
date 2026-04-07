using EFQueryLens.Core.Scripting.Evaluation;
using EFQueryLens.Lsp.Parsing;

namespace EFQueryLens.Core.Tests.Scripting;

public partial class QueryEvaluatorTests
{
    [Fact]
    public async Task Evaluate_InvalidExpression_ReturnsFailure()
    {
        var result = await TranslateV2Async("this is not valid C#");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.True(
            result.ErrorMessage.Contains("error", StringComparison.OrdinalIgnoreCase)
            || result.ErrorMessage.Contains("No DbContext subclass found", StringComparison.OrdinalIgnoreCase),
            $"Unexpected error message: {result.ErrorMessage}");
    }

    [Fact]
    public async Task Evaluate_NonQueryableExpression_ReturnsFailure()
    {
        // A literal integer produces no SQL - capture records zero commands, so the
        // engine returns a hard failure. The old "did not return an IQueryable" guard
        // was removed; the new message reflects that no SQL was captured at all.
        var result = await TranslateV2Async("42");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("no SQL commands", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Evaluate_UnknownDbContextName_ReturnsFailure()
    {
        var result = await TranslateV2Async("db.Orders", dbContextTypeName: "NoSuchContext");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("NoSuchContext", result.ErrorMessage);
    }

    [Fact]
    public async Task Evaluate_TopLevelServiceMethodInvocation_ReturnsClearUnsupportedMessage()
    {
        var result = await TranslateV2Async("service.GetWorkflowByTypeAsync(workflowType, expression, ct)");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Top-level method invocations", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Evaluate_ConcatAfterDtoProjection_FailsWithSetOperationError()
    {
        // Regression: hovering on a Concat of two branches where each branch
        // already projects to a DTO type via Select produces this EF Core error:
        //   InvalidOperationException: Unable to translate set operation after
        //   client projection has been applied. Consider moving the set operation
        //   before the last 'Select' call.
        //
        // This test captures the current (failing) baseline so a fix can be
        // verified against it. When the hover pipeline correctly hoists the
        // Select after Concat, this test must be updated to assert success.
        var expression =
            "db.Orders" +
            "    .Where(o => o.IsNotDeleted && o.Customer.CustomerId == customerId)" +
            "    .Where(o => o.CreatedUtc >= utcNow.AddDays(-7))" +
            "    .Select(o => new OrderSummaryDto(o.Id, o.Customer.Name, o.Total, o.CreatedUtc))" +
            "    .Concat(" +
            "        db.Orders" +
            "            .Where(o => o.IsNotDeleted && o.Customer.CustomerId == customerId)" +
            "            .Where(o => o.Total >= 200)" +
            "            .Select(o => new OrderSummaryDto(o.Id, o.Customer.Name, o.Total, o.CreatedUtc))" +
            "    )" +
            "    .ToListAsync(ct)";

        var result = await TranslateStrictV2Async(
            expression,
            localVariableTypes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["customerId"] = "System.Guid",
                ["utcNow"] = "System.DateTime",
                ["ct"] = "System.Threading.CancellationToken",
            },
            additionalImports: ["SampleMySqlApp.Application.Orders"],
            useAsyncRunner: true,
            ct: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("set operation", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Evaluate_ConcatAfterDtoProjection_WithLocalVariableInline_FailsWithSetOperationError()
    {
        // Regression: real-world scenario from GetHighlightOrdersAsync.
        //
        // The LSP extraction inlines local IQueryable variables into the expression.
        // After inlining, both branches project through Select, then Concat tries to
        // join them. EF Core rejects this because it cannot translate Concat after a
        // client projection has been applied.
        var expression =
            "(" +
            "    db.Orders" +
            "        .Where(o => o.IsNotDeleted && o.Customer.CustomerId == customerId)" +
            "        .Where(o => o.CreatedUtc >= utcNow.AddDays(-7))" +
            "        .Select(o => new OrderSummaryDto(o.Id, o.Customer.Name, o.Total, o.CreatedUtc))" +
            ").Concat(" +
            "    db.Orders" +
            "        .Where(o => o.IsNotDeleted && o.Customer.CustomerId == customerId)" +
            "        .Where(o => o.Total >= 200)" +
            "        .Select(o => new OrderSummaryDto(o.Id, o.Customer.Name, o.Total, o.CreatedUtc))" +
            ")" +
            ".ToListAsync(ct)";

        var result = await TranslateStrictV2Async(
            expression,
            localVariableTypes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["customerId"] = "System.Guid",
                ["utcNow"] = "System.DateTime",
                ["ct"] = "System.Threading.CancellationToken",
            },
            additionalImports: ["SampleMySqlApp.Application.Orders"],
            useAsyncRunner: true);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("set operation", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Evaluate_ConcatAfterDtoProjection_AfterPreNormalization_TranslatesSuccessfully()
    {
        var expression =
            "(" +
            "    db.Orders" +
            "        .Where(o => o.IsNotDeleted && o.Customer.CustomerId == customerId)" +
            "        .Where(o => o.CreatedUtc >= utcNow.AddDays(-7))" +
            "        .Select(o => new OrderSummaryDto(o.Id, o.Customer.Name, o.Total, o.CreatedUtc))" +
            ").Concat(" +
            "    db.Orders" +
            "        .Where(o => o.IsNotDeleted && o.Customer.CustomerId == customerId)" +
            "        .Where(o => o.Total >= 200)" +
            "        .Select(o => new OrderSummaryDto(o.Id, o.Customer.Name, o.Total, o.CreatedUtc))" +
            ")" +
            ".ToListAsync(ct)";

        var normalized = LspSyntaxHelper.PreNormalizeExtractedExpression(expression);
        var result = await TranslateStrictV2Async(
            normalized,
            localVariableTypes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["customerId"] = "System.Guid",
                ["utcNow"] = "System.DateTime",
                ["ct"] = "System.Threading.CancellationToken",
            },
            additionalImports: ["SampleMySqlApp.Application.Orders"],
            useAsyncRunner: true,
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Sql);
        Assert.Contains("UNION ALL", result.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateDbContextInstance_WhenSelectedExecutableAssemblyDiffers_RejectsFactoriesFromOtherAssemblies()
    {
        var dbContextType = _alcCtx.FindDbContextType(
            "SampleMySqlApp.Infrastructure.Persistence.MySqlAppDbContext");
        var wrongExecutableAssemblyPath = Path.Combine(
            Path.GetDirectoryName(_alcCtx.AssemblyPath)!,
            "SomeOtherHost.dll");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            QueryEvaluator.CreateDbContextInstance(
                dbContextType,
                _alcCtx.LoadedAssemblies,
                wrongExecutableAssemblyPath));

        Assert.Contains("executable project", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class library", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SomeOtherHost.dll", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
