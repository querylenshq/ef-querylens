using EFQueryLens.Lsp.Parsing;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class LinqExtractionUxTests
{
    [Fact]
    public void FindAllLinqChains_TernaryBranches_ReturnsBothChains()
    {
        var source = """
            var rows = condition
                ? await db.Orders.Where(o => o.Id == 1).ToListAsync()
                : await db.Customers.Where(c => c.Active).ToListAsync();
            """;

        var chains = LspSyntaxHelper.FindAllLinqChains(source);

        Assert.Equal(2, chains.Count);
        Assert.Contains(chains, c => c.DbSetMemberName == "Orders");
        Assert.Contains(chains, c => c.DbSetMemberName == "Customers");
    }

    [Fact]
    public void TryExtractLinqExpression_TernaryTrueBranch_ExtractsOrdersQuery()
    {
        var source = """
            var rows = condition
                ? await db.Orders.Where(o => o.Id == 1).ToListAsync()
                : await db.Customers.Where(c => c.Active).ToListAsync();
            """;

        var line = 1;
        var character = source.Split('\n')[line].IndexOf("Orders", StringComparison.Ordinal) + 3;

        var expression = LspSyntaxHelper.TryExtractLinqExpression(
            source,
            line,
            character,
            out var context,
            sourceFilePath: null);

        Assert.NotNull(expression);
        Assert.Contains("Orders", expression, StringComparison.Ordinal);
        Assert.Equal("db", context);
    }

    [Fact]
    public void TryExtractLinqExpression_TernaryFalseBranch_ExtractsCustomersQuery()
    {
        var source = """
            var rows = condition
                ? await db.Orders.Where(o => o.Id == 1).ToListAsync()
                : await db.Customers.Where(c => c.Active).ToListAsync();
            """;

        var line = 2;
        var character = source.Split('\n')[line].IndexOf("Customers", StringComparison.Ordinal) + 4;

        var expression = LspSyntaxHelper.TryExtractLinqExpression(
            source,
            line,
            character,
            out var context,
            sourceFilePath: null);

        Assert.NotNull(expression);
        Assert.Contains("Customers", expression, StringComparison.Ordinal);
        Assert.Equal("db", context);
    }

    [Fact]
    public void TryResolveHelperMethodRoots_CallSitesDoNotCrowdOutCoreDefinition()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ql-helper-{Guid.NewGuid():N}");
        var apiDir = Path.Combine(root, "Api");
        var coreDir = Path.Combine(root, "Core");
        Directory.CreateDirectory(apiDir);
        Directory.CreateDirectory(coreDir);

        try
        {
            File.WriteAllText(
                Path.Combine(apiDir, "Api.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\Core\Core.csproj" />
                  </ItemGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(coreDir, "Core.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            File.WriteAllText(
                Path.Combine(coreDir, "ApplicationService.cs"),
                """
                public class ApplicationService
                {
                    public async Task<TResult?> GetApplicationByIdAsync<TResult>(
                        Guid applicationId,
                        Expression<Func<Application, TResult>> expression,
                        CancellationToken ct)
                    {
                        return await dbContext.Applications
                            .Where(w => w.ApplicationId == applicationId)
                            .Select(expression)
                            .SingleOrDefaultAsync(ct);
                    }
                }
                """);

            for (var i = 0; i < 20; i++)
            {
                File.WriteAllText(
                    Path.Combine(apiDir, $"Caller{i}.cs"),
                    """
                    public class Caller
                    {
                        public async Task Run(ApplicationService service)
                        {
                            _ = await service.GetApplicationByIdAsync(Guid.Empty, a => a.Id, CancellationToken.None);
                        }
                    }
                    """);
            }

            var apiFile = Path.Combine(apiDir, "PrApplicationApiService.cs");
            File.WriteAllText(
                Path.Combine(apiDir, "ApplicationApiService.cs"),
                """
                public partial class ApplicationApiService(ApplicationService service);
                """);

            File.WriteAllText(
                apiFile,
                """
                public partial class ApplicationApiService
                {
                    public async Task Run(Guid applicationId, CancellationToken ct)
                    {
                        var coreData = await service.GetApplicationByIdAsync(
                            applicationId,
                            a => new
                            {
                                ProductOwners = a.PrProductOwners
                                    .Where(w => w.IsNotDeleted)
                                    .ToList(),
                            },
                            ct);
                    }
                }
                """);

            var roots = ProjectSourceHelper.TryResolveHelperMethodRoots(
                apiFile,
                File.ReadAllText(apiFile),
                "GetApplicationByIdAsync",
                "ApplicationService");

            Assert.NotEmpty(roots);
            Assert.Contains(
                roots,
                rootNode => rootNode.ToString().Contains("dbContext.Applications", StringComparison.Ordinal));

            var (line, character) = FindPosition(File.ReadAllText(apiFile), "PrProductOwners");
            var expression = LspSyntaxHelper.TryExtractLinqExpression(
                File.ReadAllText(apiFile),
                line,
                character,
                out var context,
                sourceFilePath: apiFile);

            Assert.NotNull(expression);
            Assert.Equal("dbContext", context);
            Assert.Contains("dbContext.Applications", expression, StringComparison.Ordinal);
            Assert.Contains("PrProductOwners", expression, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    [Fact]
    public void TryResolveHelperMethodRoots_CrossProjectReference_FindsCoreServiceFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ql-helper-{Guid.NewGuid():N}");
        var apiDir = Path.Combine(root, "Api");
        var coreDir = Path.Combine(root, "Core");
        Directory.CreateDirectory(apiDir);
        Directory.CreateDirectory(coreDir);

        try
        {
            File.WriteAllText(
                Path.Combine(apiDir, "Api.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\Core\Core.csproj" />
                  </ItemGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(coreDir, "Core.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            File.WriteAllText(
                Path.Combine(coreDir, "CountryService.cs"),
                """
                public class CountryService
                {
                    public IQueryable<Country> GetCountriesAsync(bool activeOnly)
                        => throw null;
                }
                """);

            var apiFile = Path.Combine(apiDir, "ApplicationApiService.cs");
            File.WriteAllText(
                apiFile,
                """
                public class ApplicationApiService
                {
                    public async Task Run(CountryService countryService)
                    {
                        var rows = await countryService.GetCountriesAsync(true).ToListAsync();
                    }
                }
                """);

            var roots = ProjectSourceHelper.TryResolveHelperMethodRoots(
                apiFile,
                File.ReadAllText(apiFile),
                "GetCountriesAsync",
                "CountryService");

            Assert.NotEmpty(roots);
            Assert.Contains(roots, rootNode => rootNode.ToString().Contains("GetCountriesAsync", StringComparison.Ordinal));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    private static (int line, int character) FindPosition(string source, string marker)
    {
        var index = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Marker '{marker}' not found in source text.");

        var line = 0;
        var character = 0;
        for (var i = 0; i < index; i++)
        {
            if (source[i] == '\n')
            {
                line++;
                character = 0;
            }
            else
            {
                character++;
            }
        }

        return (line, character);
    }
}
