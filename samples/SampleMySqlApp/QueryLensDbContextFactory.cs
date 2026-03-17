using EntityFrameworkCore.Projectables;
using Microsoft.EntityFrameworkCore;

// ReSharper disable once CheckNamespace
namespace EFQueryLens.Core
{
    public interface IQueryLensDbContextFactory<out TContext>
        where TContext : DbContext
    {
        TContext CreateOfflineContext();
    }
}

namespace SampleMySqlApp.Infrastructure.Persistence
{
    public sealed class MySqlReportingQueryLensFactory :
        EFQueryLens.Core.IQueryLensDbContextFactory<MySqlReportingDbContext>
    {
        public MySqlReportingDbContext CreateOfflineContext()
        {
            return new MySqlReportingDbContext(CreateMySqlOptions<MySqlReportingDbContext>());
        }
    
        private static DbContextOptions<TContext> CreateMySqlOptions<TContext>()
            where TContext : DbContext
        {
            // Query preview only needs provider metadata/model; no live DB call is made.
            var connectionString = "Server=ef_querylens_offline;Database=ef_querylens_offline;User Id=ef_querylens_offline;Password=ef_querylens_offline";

            var options = new DbContextOptionsBuilder<TContext>()
                .UseMySql(
                    connectionString,
                    new MySqlServerVersion(new Version(8, 0, 36)),
                    mySql => mySql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseProjectables()
                .Options;

            return options;
        }
    }

    public sealed class MySqlAppQueryLensFactory :
        EFQueryLens.Core.IQueryLensDbContextFactory<MySqlAppDbContext>
    {
        public MySqlAppDbContext CreateOfflineContext()
        {
            return new MySqlAppDbContext(CreateMySqlOptions<MySqlAppDbContext>());
        }

    
        private static DbContextOptions<TContext> CreateMySqlOptions<TContext>()
            where TContext : DbContext
        {
            // Query preview only needs provider metadata/model; no live DB call is made.
            var connectionString = "Server=ef_querylens_offline;Database=ef_querylens_offline;User Id=ef_querylens_offline;Password=ef_querylens_offline";

            var options = new DbContextOptionsBuilder<TContext>()
                .UseMySql(
                    connectionString,
                    new MySqlServerVersion(new Version(8, 0, 36)),
                    mySql => mySql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseProjectables()
                .Options;

            return options;
        }
    }

}