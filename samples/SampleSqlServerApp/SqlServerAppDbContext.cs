using Microsoft.EntityFrameworkCore;
using SampleSqlServerApp.Entities;

namespace SampleSqlServerApp;

public class SqlServerAppDbContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();

    public SqlServerAppDbContext(DbContextOptions<SqlServerAppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(b =>
        {
            b.ToTable("Customers");
            b.Property(c => c.Name).HasMaxLength(200);
        });
    }
}
