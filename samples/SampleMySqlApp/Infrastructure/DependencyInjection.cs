using EntityFrameworkCore.Projectables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SampleMySqlApp.Application.Abstractions;
using SampleMySqlApp.Infrastructure.Persistence;

namespace SampleMySqlApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MySqlAppDbContext>(options =>
            options
                .UseMySql(
                    "Name=MainConnection",
                    new MySqlServerVersion(new Version(8, 0, 36)),
                    mySql => mySql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseProjectables());

        services.AddScoped<IMySqlAppDbContext>(sp => sp.GetRequiredService<MySqlAppDbContext>());
        return services;
    }
}
