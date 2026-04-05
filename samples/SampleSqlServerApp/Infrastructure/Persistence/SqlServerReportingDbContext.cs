using EntityFrameworkCore.Projectables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SampleSqlServerApp.Domain.Entities;

namespace SampleSqlServerApp.Infrastructure.Persistence;

public sealed class SqlServerReportingDbContext : DbContext
{
    public DbSet<Customer> CustomerDirectory => Set<Customer>();

    public SqlServerReportingDbContext(DbContextOptions<SqlServerReportingDbContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured)
        {
            return;
        }

        optionsBuilder
            .UseSqlServer(
                ResolveMainConnectionString(),
                sqlServer => sqlServer.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseProjectables();
    }

    private static string ResolveMainConnectionString()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var resolved = configuration.GetConnectionString("MainConnection")
                ?? configuration["ConnectionStrings:MainConnection"];

            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }
        catch
        {
            // Fall through to deterministic offline-safe connection.
        }

        return "Server=ef_querylens_offline;Database=ef_querylens_offline;User Id=ef_querylens_offline;Password=ef_querylens_offline;TrustServerCertificate=True";
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(b =>
        {
            b.ToTable("Customers");
            b.HasKey(x => x.Id);
            b.Property(x => x.CustomerId);
            b.HasIndex(x => x.CustomerId).IsUnique();
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.Email).HasMaxLength(320);
            b.Property(x => x.IsDeleted).HasDefaultValue(false);
            b.Property(x => x.IsActive).HasDefaultValue(true);
            b.Property(x => x.CreatedUtc);
        });
    }
}
