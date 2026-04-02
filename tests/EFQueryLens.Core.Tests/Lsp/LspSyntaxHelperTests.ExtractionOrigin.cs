using EFQueryLens.Lsp.Parsing;

namespace EFQueryLens.Core.Tests.Lsp;

public partial class LspSyntaxHelperTests
{
    [Fact]
    public void TryExtractLinqExpressionDetailed_HelperQueryableCall_UsesHelperOrigin()
    {
        var source = """
            class CustomerReadService
            {
                public List<int> GetIds(Guid customerId)
                {
                    return BuildRecentOrdersQuery(customerId).Select(o => o.Id).ToList();
                }

                IQueryable<Order> BuildRecentOrdersQuery(Guid id)
                {
                    var page = 2;
                    return _dbContext.Orders
                        .Where(o => o.Customer.CustomerId == id)
                        .Skip((page - 1) * 10)
                        .Take(10);
                }
            }
            """;

        var (line, character) = FindPosition(source, "BuildRecentOrdersQuery(customerId).Select");
        var result = LspSyntaxHelper.TryExtractLinqExpressionDetailed(
            source,
            filePath: @"c:\repo\CustomerReadService.cs",
            line,
            character);

        Assert.NotNull(result);
        Assert.Equal("_dbContext", result.ContextVariableName);
        Assert.Equal("helper-method", result.Origin.Scope);
        Assert.NotEqual(line, result.Origin.Line);
        Assert.Contains("_dbContext.Orders", result.Expression, StringComparison.Ordinal);
        Assert.Contains(".Where(o => o.Customer.CustomerId == customerId)", result.Expression, StringComparison.Ordinal);
    }

    [Fact]
    public void TryExtractLinqExpressionDetailed_DoesNotInjectSemanticCommentHints()
    {
        var source = """
            class Demo
            {
                void M(Guid customerId)
                {
                    var q = _dbContext.Orders
                        .Where(o => o.Customer.CustomerId == customerId)
                        .Where(o => o.Customer.CustomerId == customerId);
                }
            }
            """;

        var (line, character) = FindPosition(source, ".Where(o => o.Customer.CustomerId == customerId);");
        var result = LspSyntaxHelper.TryExtractLinqExpressionDetailed(
            source,
            filePath: @"c:\repo\Demo.cs",
            line,
            character);

        Assert.NotNull(result);
        Assert.DoesNotContain("// var ", result.Expression, StringComparison.Ordinal);
        Assert.True(result.Origin.Line >= 0);
        Assert.True(result.Origin.Character >= 0);
    }
}
