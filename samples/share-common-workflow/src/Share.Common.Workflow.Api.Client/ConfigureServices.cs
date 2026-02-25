using Microsoft.Extensions.DependencyInjection;
using Share.Lib.Kiota.Client;

namespace Share.Common.Workflow.Api.Client;

/// <summary>
/// Provides extension methods for registering services related to the Workflow API client in the dependency injection container.
/// </summary>
public static class ConfigureServices
{
    /// <summary>
    /// Adds the WorkflowApiClient to the service collection with the specified base address, app code, and app secret.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseAddress">The base address of the API.</param>
    /// <param name="appCode">The app code.</param>
    /// <param name="appSecret">The app secret.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddWorkflowApiClient(
        this IServiceCollection services,
        string baseAddress,
        string appCode,
        string appSecret
    )
    {
        services.AddShareApiBasicAuthClient<WorkflowApiClient>(baseAddress, appCode, appSecret);

        return services;
    }
}
