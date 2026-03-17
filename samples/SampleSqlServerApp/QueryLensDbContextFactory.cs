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

namespace SampleSqlServerApp.Infrastructure.Persistence
{
    public sealed class SqlServerAppQueryLensFactory :
        EFQueryLens.Core.IQueryLensDbContextFactory<SqlServerAppDbContext>,
        EFQueryLens.Core.IQueryLensDbContextFactory<SqlServerReportingDbContext>
    {
        public SqlServerAppDbContext CreateOfflineContext()
        {
            return new SqlServerAppDbContext(CreateSqlServerOptions<SqlServerAppDbContext>());
        }

        SqlServerReportingDbContext EFQueryLens.Core.IQueryLensDbContextFactory<SqlServerReportingDbContext>.CreateOfflineContext()
        {
            return new SqlServerReportingDbContext(CreateSqlServerOptions<SqlServerReportingDbContext>());
        }

        private static DbContextOptions<TContext> CreateSqlServerOptions<TContext>()
            where TContext : DbContext
        {
            // SQL preview only needs provider metadata/model; no live DB call is made.
            var connectionString = "Server=ef_querylens_offline;Database=ef_querylens_offline;User Id=ef_querylens_offline;Password=ef_querylens_offline;TrustServerCertificate=True";

            var options = new DbContextOptionsBuilder<TContext>()
                .UseSqlServer(
                    connectionString,
                    sqlServer => sqlServer.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseProjectables()
                .Options;

            return options;
        }
    }
}