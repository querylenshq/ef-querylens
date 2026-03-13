using Microsoft.EntityFrameworkCore;
using SamplePostgresApp.Domain.Entities;

namespace SamplePostgresApp.Infrastructure.Persistence;

public sealed class PostgresReportingDbContext : DbContext
{
    public DbSet<Customer> CustomerDirectory => Set<Customer>();

    public PostgresReportingDbContext(DbContextOptions<PostgresReportingDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(b =>
        {
            b.ToTable("customers");
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
