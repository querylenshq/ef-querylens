namespace Microsoft.EntityFrameworkCore.Design;

public interface IDesignTimeDbContextFactory<out TContext>
{
    TContext CreateDbContext(string[] args);
}
