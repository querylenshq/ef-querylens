using Kiota.Builder;
using MassTransit;
using Serilog;
using Share.Common.Workflow.Api.Services;
using Share.Lib.Abstractions.Common.Interfaces;
using Share.Lib.Abstractions.Common.Services;
using Share.Lib.Abstractions.FastEndpoints;

namespace Share.Common.Workflow.Api;

public static class ConfigureServices
{
    public static IServiceCollection AddWorkflowApiServices(this IServiceCollection services)
    {
        services
            .AddOptions<CurrentUserOptions>()
            .BindConfiguration(nameof(CurrentUserOptions))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IMedicsCurrentUser, MedicsCurrentUser>();
        services.ReplaceScoped<ICurrentUser, MedicsCurrentUser>();
        return services;
    }

    public static async Task GenerateApiClientAsync(this WebApplication app)
    {
        var path = app.Environment.ContentRootPath;
        path = Directory.GetParent(path)!.FullName;
        path = Path.Combine(path, "Share.Common.Workflow.Api.Client", "Store");
        Log.Logger.Information("Generating Api Client for Share.Common.Workflow.Api {Path}", path);
        await app.GenerateShareApiClientsAsync(
            "v1",
            "Share.Common.Workflow.Api.Client",
            "WorkflowApiClient",
            path,
            config =>
            {
                config.ExcludeBackwardCompatible = true;
                config.IncludeAdditionalData = false;
                config.Language = GenerationLanguage.CSharp;
                config.ExcludePatterns.Add("**/dev/**");
                config.CleanOutput = false;
            }
        );
    }
}
