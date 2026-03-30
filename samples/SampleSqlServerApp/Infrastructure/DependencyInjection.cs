using EntityFrameworkCore.Projectables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SampleSqlServerApp.Application.Abstractions;
using SampleSqlServerApp.Application.Reporting;
using SampleSqlServerApp.Infrastructure.Persistence;

namespace SampleSqlServerApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SqlServerAppDbContext>(options =>
            options
                .UseSqlServer(
                    "Name=MainConnection",
                    sqlServer => sqlServer.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseProjectables());

        services.AddScoped<ISqlServerAppDbContext>(sp => sp.GetRequiredService<SqlServerAppDbContext>());

        services.AddDbContext<SqlServerReportingDbContext>(options =>
            options
                .UseSqlServer(
                    "Name=MainConnection",
                    sqlServer => sqlServer.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseProjectables());

  

        return services;
    }
}
