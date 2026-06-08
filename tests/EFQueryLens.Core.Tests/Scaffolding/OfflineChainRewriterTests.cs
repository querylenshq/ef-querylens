using EFQueryLens.Core.Scaffolding;

namespace EFQueryLens.Core.Tests.Scaffolding;

public sealed class OfflineChainRewriterTests
{
    [Fact]
    public void Rewrite_SampleMySqlRegistration_ReplacesConnectionStringOnly()
    {
        var registration = new DbContextRegistration
        {
            ContextTypeName = "MySqlAppDbContext",
            BuilderParameterName = "options",
            OptionsChain =
                ".UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)), mySql => mySql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)).UseProjectables()",
            Usings =
            [
                "EntityFrameworkCore.Projectables",
                "Microsoft.EntityFrameworkCore",
            ],
        };

        var result = OfflineChainRewriter.Rewrite(registration);

        Assert.Equal(ProviderKind.MySql, result.Provider);
        Assert.True(result.UseProjectables);
        Assert.True(result.UseSplitQuery);
        Assert.Contains("Server=ef_querylens_offline", result.OfflineChain, StringComparison.Ordinal);
        Assert.Contains("new MySqlServerVersion(new Version(8, 0, 36))", result.OfflineChain, StringComparison.Ordinal);
        Assert.Contains("UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)", result.OfflineChain, StringComparison.Ordinal);
        Assert.Contains(".UseProjectables()", result.OfflineChain, StringComparison.Ordinal);
        Assert.DoesNotContain("connectionString", result.OfflineChain, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".UseSqlServer(connectionString)", ProviderKind.SqlServer, "Server=ef_querylens_offline")]
    [InlineData(".UseNpgsql(connectionString)", ProviderKind.Npgsql, "Host=ef_querylens_offline")]
    [InlineData(".UseSqlite(\"prod.db\")", ProviderKind.Sqlite, "Data Source=:memory:")]
    public void RewriteChain_ReplacesProviderConnectionArgument(string chain, ProviderKind provider, string expectedFragment)
    {
        var result = OfflineChainRewriter.RewriteChain(chain, ["Microsoft.EntityFrameworkCore"]);

        Assert.Equal(provider, result.Provider);
        Assert.Contains(expectedFragment, result.OfflineChain, StringComparison.Ordinal);
    }

    [Fact]
    public void DetectProvider_RecognizesEachProviderToken()
    {
        Assert.Equal(ProviderKind.SqlServer, OfflineChainRewriter.DetectProvider(".UseSqlServer(\"x\")"));
        Assert.Equal(ProviderKind.Npgsql, OfflineChainRewriter.DetectProvider(".UseNpgsql(\"x\")"));
        Assert.Equal(ProviderKind.MySql, OfflineChainRewriter.DetectProvider(".UseMySql(\"x\", version)"));
        Assert.Equal(ProviderKind.Sqlite, OfflineChainRewriter.DetectProvider(".UseSqlite(\"x\")"));
        Assert.Equal(ProviderKind.Unknown, OfflineChainRewriter.DetectProvider(".UseInMemoryDatabase(\"x\")"));
    }

    [Fact]
    public void ReplaceConnectionStringArgument_PreservesSecondArgumentForMySql()
    {
        var chain =
            ".UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)), mySql => mySql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))";

        var rewritten = OfflineChainRewriter.ReplaceConnectionStringArgument(chain, ProviderKind.MySql);

        Assert.Contains("Server=ef_querylens_offline", rewritten, StringComparison.Ordinal);
        Assert.Contains("new MySqlServerVersion(new Version(8, 0, 36))", rewritten, StringComparison.Ordinal);
        Assert.Contains("UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)", rewritten, StringComparison.Ordinal);
    }
}
