using EFQueryLens.Lsp.Parsing;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class SemanticExpressionRangeTests
{
    [Fact]
    public void TryExtractLinqExpression_MultiLineChain_MiddleLine_FindsQuery()
    {
        var source = """
            return await dbContext
                .Countries.AsNoTracking()
                .Where(w => w.IsNotDeleted)
                .Where(w => countryIds.Contains(w.CountryId))
                .OrderBy(o => o.Name)
                .Select(selectExpression)
                .ToListAsync(ct);
            """;

        var whereLine = source.Split('\n').ToList().FindIndex(l => l.Contains(".Where(w => w.IsNotDeleted)", StringComparison.Ordinal));
        Assert.True(whereLine >= 0);

        var lineText = source.Split('\n')[whereLine];
        var character = lineText.IndexOf("Where", StringComparison.Ordinal) + 2;

        var expression = LspSyntaxHelper.TryExtractLinqExpression(
            source,
            whereLine,
            character,
            out var context,
            sourceFilePath: null);

        Assert.NotNull(expression);
        Assert.Contains("Countries", expression, StringComparison.Ordinal);
        Assert.Equal("dbContext", context);
    }
}
