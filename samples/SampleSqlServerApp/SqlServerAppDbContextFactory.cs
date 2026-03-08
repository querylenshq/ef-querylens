using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SampleSqlServerApp;

public class SqlServerAppDbContextFactory : IDesignTimeDbContextFactory<SqlServerAppDbContext>
{
    public SqlServerAppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SqlServerAppDbContext>()
            .UseSqlServer("Server=127.0.0.1,1433;Database=__querylens__;User Id=sa;Password=QueryLens123!;TrustServerCertificate=True")
            .Options;

        return new SqlServerAppDbContext(options);
    }
}
