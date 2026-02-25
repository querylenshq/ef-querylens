using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Share.Common.AccessControl.Auth.Api.Client;
using Share.Common.Workflow.Core.Application.Services;
using Share.Common.Workflow.Core.Infrastructure.Interfaces;
using Share.Common.Workflow.Core.Infrastructure.Services;
using Share.Lib.Bootstrap.Api.Core;
using Share.Lib.Cloud.StackExchangeRedis;

namespace Share.Common.Workflow.Core;

public static class ConfigureServices
{
    public static IServiceCollection AddWorkflowCoreInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddShareStackExchangeRedisCache(configuration);

        services.AddShareAuthPluginCore<IWorkflowDbContext, WorkflowDbContext>(
            Domain.Constants.Configurations.ConnectionStrings.ApplicationConnectionString
        );
        return services;
    }

    public static IServiceCollection AddWorkflowApplicationServices(
        this IServiceCollection services,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        string baseUrl
    )
    {
        services.AddScoped<WorkflowService>();
        services.AddScoped<ApplicationWorkflowService>();
        services.AddScoped<WorkflowAccountService>();

        #region Required for Auth sync
        services.AddScoped<AccessAuthApiService>();
        services.AddAccessAuthApiClient(baseUrl);
        #endregion

        return services;
    }
}
