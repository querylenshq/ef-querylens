using System.Text.RegularExpressions;
using EFQueryLens.Lsp.Parsing;

namespace EFQueryLens.Core.Tests.Lsp;

public partial class LspSyntaxHelperTests
{
    [Fact]
    public void TryExtractLinqExpression_GoldenCase_SimpleTerminal_ShouldDetectExpectedExpression()
    {
        AssertExtractionCase(CreateSimpleTerminalCase());
    }

    [Fact]
    public void TryExtractLinqExpression_GoldenCase_IfElseSelfAppend_ShouldDetectExpectedExpression()
    {
        AssertExtractionCase(CreateIfElseSelfAppendCase());
    }

    [Fact]
    public void TryExtractLinqExpression_GoldenCase_HelperPassthrough_ShouldDetectExpectedExpression()
    {
        AssertExtractionCase(CreateHelperPassthroughCase());
    }

    [Fact]
    public void TryExtractLinqExpression_GoldenCase_TernarySelfAppend_ShouldDetectExpectedExpression()
    {
        AssertExtractionCase(CreateTernarySelfAppendCase());
    }

    [Fact]
    public void TryExtractLinqExpression_GoldenCase_VariableInitializerConcat_ShouldDetectExpectedExpression()
    {
        AssertExtractionCase(CreateVariableInitializerConcatCase());
    }

    [Fact]
    public void TryExtractLinqExpression_GoldenCase_UnknownQueryableOperator_StrictModeDoesNotDetectExpression()
    {
        AssertExtractionCase(CreateUnknownQueryableOperatorCase());
    }

    [Fact]
    public void TryExtractLinqExpression_GoldenCases_ShouldNotDetectNonQueryCall()
    {
        var source = """
            var x = string.Join(",", items.Select(i => i.Code));
            """;

        var (line, character) = FindPosition(source, "Join");

        var expression = LspSyntaxHelper.TryExtractLinqExpression(source, line, character, out var contextVariableName,
            out _);

        Assert.True(
            string.IsNullOrWhiteSpace(expression),
            $"Expected no extraction for non-query call but got: {expression}");
        Assert.True(
            string.IsNullOrWhiteSpace(contextVariableName),
            $"Expected no context variable for non-query call but got: {contextVariableName}");
    }

    private static void AssertExtractionCase(ExtractionCase testCase)
    {
        var (line, character) = FindPosition(testCase.Source, testCase.Marker);

        var expression = LspSyntaxHelper.TryExtractLinqExpression(
            testCase.Source,
            line,
            character,
            out var contextVariableName,
            out _);

        if (!testCase.ShouldDetect)
        {
            Assert.True(
                string.IsNullOrWhiteSpace(expression),
                BuildFailureMessage(testCase, expression, contextVariableName));
            return;
        }

        Assert.False(string.IsNullOrWhiteSpace(expression), BuildFailureMessage(testCase, expression, contextVariableName));
        Assert.Equal(testCase.ExpectedContext, contextVariableName);

        var normalizedActual = NormalizeExpression(expression);

        if (!string.IsNullOrWhiteSpace(testCase.ExpectedExpression))
        {
            var normalizedExpected = NormalizeExpression(testCase.ExpectedExpression);
            Assert.Equal(
                normalizedExpected,
                normalizedActual);
        }

        if (testCase.ExpectedExpressionContains is null)
        {
            return;
        }

        foreach (var expectedFragment in testCase.ExpectedExpressionContains)
        {
            Assert.Contains(expectedFragment, expression, StringComparison.Ordinal);
        }
    }

    private static string NormalizeExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return string.Empty;
        }

        return Regex.Replace(expression, "\\s+", " ").Trim();
    }

    private static string BuildFailureMessage(ExtractionCase testCase, string? actualExpression, string? contextVariableName)
    {
        return $"""
            Golden extraction mismatch: {testCase.Name}
            Marker: {testCase.Marker}
            ShouldDetect: {testCase.ShouldDetect}
            ExpectedContext: {testCase.ExpectedContext}
            ActualContext: {contextVariableName}
            ExpectedExpression: {testCase.ExpectedExpression}
            ActualExpression: {actualExpression}
            Source:
            {testCase.Source}
            """;
    }

    private static ExtractionCase CreateSimpleTerminalCase()
    {
        return new ExtractionCase(
            Name: "simple-terminal",
            Source: """
                var data = dbContext.Orders.Where(o => o.UserId == userId).ToList();
                """,
            Marker: "ToList",
            ShouldDetect: true,
            ExpectedContext: "dbContext",
            ExpectedExpression: "dbContext.Orders.Where(o => o.UserId == userId).ToList()");
    }

    private static ExtractionCase CreateIfElseSelfAppendCase()
    {
        return new ExtractionCase(
            Name: "if-else-self-append",
            Source: """
                var query = items.Where(i => i.Id > minId);

                if (filter.IncludeCode)
                {
                    query = query.Where(i => i.Code.StartsWith("A"));
                }
                else
                {
                    query = query.Where(i => i.Name.Contains("Test"));
                }

                var data = query.Select(i => i.Id).ToList();
                """,
            Marker: "ToList",
            ShouldDetect: true,
            ExpectedContext: "items",
            ExpectedExpressionContains:
            [
                "items.Where(i => i.Id > minId)",
                "Name.Contains(\"Test\")",
                ".Select(i => i.Id).ToList()",
            ]);
    }

    private static ExtractionCase CreateHelperPassthroughCase()
    {
        return new ExtractionCase(
            Name: "helper-passthrough",
            Source: """
                var query = items.Where(i => i.Id > minId);
                query = SomeMethodThatAddsMoreFilters(query);
                return query.Select(i => i.Id);
                """,
            Marker: "Select(i => i.Id)",
            ShouldDetect: true,
            ExpectedContext: "items",
            ExpectedExpressionContains:
            [
                "SomeMethodThatAddsMoreFilters(items.Where(i => i.Id > minId))",
                ".Select(i => i.Id)",
            ]);
    }

    private static ExtractionCase CreateTernarySelfAppendCase()
    {
        return new ExtractionCase(
            Name: "ternary-self-append",
            Source: """
                var query = items.Where(i => i.Id > 0);
                query = sortByCode ? query.OrderBy(i => i.Code) : query.OrderBy(i => i.CreatedUtc);
                return query.Select(i => i.Id);
                """,
            Marker: "Select(i => i.Id)",
            ShouldDetect: true,
            ExpectedContext: "items",
            ExpectedExpressionContains:
            [
                "OrderBy(i => i.CreatedUtc)",
            ]);
    }

    private static ExtractionCase CreateVariableInitializerConcatCase()
    {
        return new ExtractionCase(
            Name: "variable-initializer-concat",
            Source: """
                var recentOrders = _dbContext.Orders
                    .Where(o => o.IsNotDeleted && o.Customer.CustomerId == customerId)
                    .Where(o => o.CreatedUtc >= utcNow.Date.AddDays(-7))
                    .Select(o => new OrderSummaryDto(o.Id, o.Customer.Name, o.Total, o.CreatedUtc));
                var highValueOrders = _dbContext.Orders
                    .Where(o => o.IsNotDeleted && o.Customer.CustomerId == customerId)
                    .Where(o => o.Total >= 200)
                    .Select(o => new OrderSummaryDto(o.Id, o.Customer.Name, o.Total, o.CreatedUtc));
                var highlightOrders = recentOrders.Concat(highValueOrders);
                """,
            Marker: "highlightOrders",
            ShouldDetect: true,
            ExpectedContext: "_dbContext",
            ExpectedExpressionContains:
            [
                "_dbContext.Orders",
                ".Concat(",
                "o.CreatedUtc >= utcNow.Date.AddDays(-7)",
                "o.Total >= 200",
            ]);
    }

    private static ExtractionCase CreateUnknownQueryableOperatorCase()
    {
        return new ExtractionCase(
            Name: "unknown-queryable-operator",
            Source: """
                var query = items.Where(i => i.Id > minId);
                var secured = query.ApplySecurityScope(currentUserId);
                """,
            Marker: "ApplySecurityScope",
            ShouldDetect: false);
    }

    private sealed record ExtractionCase(
        string Name,
        string Source,
        string Marker,
        bool ShouldDetect,
        string? ExpectedContext = null,
        string? ExpectedExpression = null,
        IReadOnlyList<string>? ExpectedExpressionContains = null);
}
