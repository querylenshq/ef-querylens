using EntityFrameworkCore.Projectables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
    public sealed class SqlServerAppDbContextFactory :
        EFQueryLens.Core.IQueryLensDbContextFactory<SqlServerAppDbContext>
    {
        private const string OfflineConnectionString =
            "Server=ef_querylens_offline;Database=ef_querylens_offline;User Id=ef_querylens_offline;Password=ef_querylens_offline;TrustServerCertificate=True";

        public SqlServerAppDbContext CreateOfflineContext()
        {
            return new SqlServerAppDbContext(CreateSqlServerOptions<SqlServerAppDbContext>());
        }

        private static DbContextOptions<TContext> CreateSqlServerOptions<TContext>()
            where TContext : DbContext
        {
            var connectionString = "Name=MainConnection";
            var applicationServiceProvider = CreateApplicationServiceProvider();
            return new DbContextOptionsBuilder<TContext>()
                .UseApplicationServiceProvider(applicationServiceProvider)
                .UseSqlServer(connectionString,
                    sqlServer => sqlServer.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseProjectables()
                .Options;
        }

        private static IServiceProvider CreateApplicationServiceProvider()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MainConnection"] = OfflineConnectionString,
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            return services.BuildServiceProvider();
        }
    }

    public sealed class SqlServerReportingDbContextFactory :
        EFQueryLens.Core.IQueryLensDbContextFactory<SqlServerReportingDbContext>
    {
        public SqlServerReportingDbContext CreateOfflineContext()
        {
            return new SqlServerReportingDbContext(CreateSqlServerOptions<SqlServerReportingDbContext>());
        }

        private static DbContextOptions<TContext> CreateSqlServerOptions<TContext>()
            where TContext : DbContext
        {
            var connectionString = "Server=ef_querylens_offline;Database=ef_querylens_offline;User Id=ef_querylens_offline;Password=ef_querylens_offline;TrustServerCertificate=True";
            return new DbContextOptionsBuilder<TContext>()
                .UseSqlServer(connectionString,
                    sqlServer => sqlServer.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseProjectables()
                .Options;
        }
    }
}
