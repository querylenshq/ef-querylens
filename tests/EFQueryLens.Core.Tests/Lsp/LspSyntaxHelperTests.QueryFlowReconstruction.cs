using EFQueryLens.Lsp.Parsing;

namespace EFQueryLens.Core.Tests.Lsp;

public partial class LspSyntaxHelperTests
{
    [Fact]
    public void TryExtractLinqExpression_IfElseSelfAppend_DefaultsToElseBranch()
    {
        var source = """
            public string GetSomeValues(IQueryable<Item> items, int minId, CustomFilter filter)
            {
                var query = items.Where(i => i.Id > minId);

                if (filter.IncludeCode)
                {
                    query = query.Where(i => i.Code.StartsWith("A"));
                }
                else
                {
                    query = query.Where(i => i.Name.Contains("Test"));
                }

                var data = query
                    .Select(i => new { i.Id, i.Code, i.Name })
                    .ToList();

                return string.Join(",", data.Select(d => d.Code));
            }
            """;

        var (line, character) = FindPosition(source, "ToList");

        var expression = LspSyntaxHelper.TryExtractLinqExpression(
            source,
            line,
            character,
            out var contextVariableName,
            out _);

        Assert.NotNull(expression);
        Assert.Equal("items", contextVariableName);
        Assert.Contains("Name.Contains(\"Test\")", expression, StringComparison.Ordinal);
        Assert.DoesNotContain("filter.IncludeCode?", expression, StringComparison.Ordinal);
        Assert.DoesNotContain("Code.StartsWith(\"A\")", expression, StringComparison.Ordinal);
    }

    [Fact]
    public void TryExtractLinqExpression_HelperPassthrough_AssignmentPreservesPriorChain()
    {
        var source = """
            public IQueryable<Item> Build(IQueryable<Item> items, int minId)
            {
                var query = items.Where(i => i.Id > minId);
                query = SomeMethodThatAddsMoreFilters(query);

                return query.Select(i => i.Id);
            }
            """;

        var (line, character) = FindPosition(source, "Select(i => i.Id)");

        var expression = LspSyntaxHelper.TryExtractLinqExpression(
            source,
            line,
            character,
            out var contextVariableName,
            out _);

        Assert.NotNull(expression);
        Assert.Equal("items", contextVariableName);
        Assert.Contains("SomeMethodThatAddsMoreFilters(items.Where(i => i.Id > minId))", expression, StringComparison.Ordinal);
    }

    [Fact]
    public void TryExtractLinqExpression_TernarySelfAppend_DefaultsToElseBranch()
    {
        var source = """
            public IQueryable<Item> Build(IQueryable<Item> items, bool sortByCode)
            {
                var query = items.Where(i => i.Id > 0);
                query = sortByCode ? query.OrderBy(i => i.Code) : query.OrderBy(i => i.CreatedUtc);
                return query.Select(i => i.Id);
            }
            """;

        var (line, character) = FindPosition(source, "Select(i => i.Id)");

        var expression = LspSyntaxHelper.TryExtractLinqExpression(
            source,
            line,
            character,
            out var contextVariableName,
            out _);

        Assert.NotNull(expression);
        Assert.Equal("items", contextVariableName);
        Assert.Contains("OrderBy(i => i.CreatedUtc)", expression, StringComparison.Ordinal);
        Assert.DoesNotContain("sortByCode?", expression, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderBy(i => i.Code)", expression, StringComparison.Ordinal);
    }

    [Fact]
    public void TryExtractLinqExpression_TernaryInlineQueries_HoverOnTrueBranch_ExtractsTrueBranchQuery()
    {
        var source = """
            public IQueryable<Item> Build(MyDbContext dbContext, bool useRecent)
            {
                var selected = useRecent
                    ? dbContext.Items.Where(i => i.CreatedUtc >= cutoffUtc)
                    : dbContext.Items.Where(i => i.Total >= 200m);
                return selected.Select(i => i.Id);
            }
            """;

        var (line, character) = FindPosition(source, "CreatedUtc >= cutoffUtc");

        var expression = LspSyntaxHelper.TryExtractLinqExpression(
            source,
            line,
            character,
            out var contextVariableName,
            out _);

        Assert.NotNull(expression);
        Assert.Equal("dbContext", contextVariableName);
        Assert.Contains("CreatedUtc >= cutoffUtc", expression, StringComparison.Ordinal);
        Assert.DoesNotContain("Total >= 200m", expression, StringComparison.Ordinal);
    }

    [Fact]
    public void TryExtractLinqExpression_TernaryQueryVariables_HoverOnTrueBranchIdentifier_ExtractsResolvedTrueQuery()
    {
        var source = """
            public IQueryable<Item> Build(MyDbContext dbContext, bool useRecent)
            {
                var recentOrders = dbContext.Items.Where(i => i.CreatedUtc >= cutoffUtc);
                var highValueOrders = dbContext.Items.Where(i => i.Total >= 200m);
                var selected = useRecent ? recentOrders : highValueOrders;
                return selected.Select(i => i.Id);
            }
            """;

        var (line, character) = FindPosition(source, "recentOrders : highValueOrders");

        var expression = LspSyntaxHelper.TryExtractLinqExpression(
            source,
            line,
            character,
            out var contextVariableName,
            out _);

        Assert.NotNull(expression);
        Assert.Equal("dbContext", contextVariableName);
        Assert.Contains("CreatedUtc >= cutoffUtc", expression, StringComparison.Ordinal);
        Assert.DoesNotContain("Total >= 200m", expression, StringComparison.Ordinal);
    }
}
