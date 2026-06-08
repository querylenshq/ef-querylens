using EFQueryLens.Core.Scaffolding;

namespace EFQueryLens.Core.Tests.Scaffolding;

public sealed class RegistrationScannerTests
{
    private static string SampleMySqlProjectDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "SampleMySqlApp"));

    [Fact]
    public void Scan_SampleMySqlApp_FindsMySqlAppDbContextRegistration()
    {
        var registrations = RegistrationScanner.Scan(SampleMySqlProjectDir);

        var registration = Assert.Single(
            registrations,
            r => r.ContextTypeName.EndsWith("MySqlAppDbContext", StringComparison.Ordinal));

        Assert.Equal("options", registration.BuilderParameterName);
        Assert.Contains(".UseMySql(", registration.OptionsChain, StringComparison.Ordinal);
        Assert.Contains("UseQuerySplittingBehavior", registration.OptionsChain, StringComparison.Ordinal);
        Assert.Contains("UseProjectables", registration.OptionsChain, StringComparison.Ordinal);
        Assert.Contains("MySqlServerVersion", registration.OptionsChain, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_SampleMySqlApp_IncludesSolutionProjects()
    {
        var directories = RegistrationScanner.ResolveScanDirectories(SampleMySqlProjectDir);

        Assert.Contains(
            Path.GetFullPath(SampleMySqlProjectDir),
            directories.Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void StripBuilderPrefix_RemovesParameterPrefix()
    {
        var chain = RegistrationScanner.StripBuilderPrefix(
            "options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)))",
            "options");

        Assert.StartsWith(".UseMySql(", chain, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_TempProjectWithAddDbContextPool_FindsRegistration()
    {
        var dir = CreateTempProject(
            """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            public class AppDbContext : DbContext
            {
                public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
            }

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddDbContextPool<AppDbContext>(options =>
                        options.UseSqlite("Data Source=app.db"));
                }
            }
            """);

        try
        {
            var registrations = RegistrationScanner.Scan(dir);

            var registration = Assert.Single(registrations);
            Assert.Equal("AppDbContext", registration.ContextTypeName);
            Assert.Equal(".UseSqlite(\"Data Source=app.db\")", registration.OptionsChain);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateTempProject(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ql-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Program.cs"), source);
        return dir;
    }
}
