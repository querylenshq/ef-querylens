using Microsoft.EntityFrameworkCore;
using SampleSqlServerApp.Domain.Entities;

namespace SampleSqlServerApp.Infrastructure.Persistence;

public sealed class SqlServerReportingDbContext : DbContext
{
    public DbSet<Customer> CustomerDirectory => Set<Customer>();

    public SqlServerReportingDbContext(DbContextOptions<SqlServerReportingDbContext> options)
        : base(options)
    {
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
