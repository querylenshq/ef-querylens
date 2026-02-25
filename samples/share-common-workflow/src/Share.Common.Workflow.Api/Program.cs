using System.Diagnostics;
using Serilog;
using Share.Common.Workflow.Api;
using Share.Common.Workflow.Api.Consumers;
using Share.Common.Workflow.Core;
using Share.Common.Workflow.Core.Domain.Options;
using Share.Lib.Abstractions.Api;
using Share.Lib.Abstractions.Aspire.ServiceDefaults;
using Share.Lib.Abstractions.Common;
using Share.Lib.Abstractions.FastEndpoints;
using Share.Lib.Abstractions.Serilog;
using Share.Lib.Bootstrap.Api;
using Share.Lib.Cloud.SecretsManager;
using Share.Lib.MassTransit.AmazonSqs;

ConfigureShareSerilog.CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Configuration.AddSecretsManager();
    builder.AddServiceDefaults();

    await builder.ConfigureMedicsTls();

    var services = builder.Services;
    var configuration = builder.Configuration;

    services.AddShareSerilog(preserveStaticLogger: true);

    services.AddShareFastEndpoints();
    services.AddShareFastEndpointSwaggerDocument("Share.Common.Workflow.Api", "v1");
    services.AddShareCommonCoreApiServices();
    services.AddWorkflowApiServices();

    services.AddShareCommonCoreServices();
    services.AddWorkflowApiServices();
    var opts = configuration.GetSection(nameof(WorkflowApiOptions)).Get<WorkflowApiOptions>()!;
    services.AddWorkflowCoreInfrastructure(configuration);

    // Ensure this registered earlier than another api client (if any)
    services.AddWorkflowApplicationServices(builder.Environment, configuration, opts.BaseUrl);

    var secretKey = configuration.GetValue<string>("HmacBearer:Key")!;
    var appcode = configuration.GetValue<string>("HmacBearer:AppCode")!;

    services.AddApiAuthPluginServices(
        secretKey,
        appcode,
        "common_workflow_permission_details.json",
        _ => Task.CompletedTask
    );

    services.AddShareMassTransitAmazonSqs(
        configuration,
        cfg =>
        {
            cfg.AddConsumer<AccountDetailsConsumer>();
        },
        (context, aws) =>
        {
            var config = configuration
                .GetSection(nameof(WorkflowSqsSnsOptions))
                .Get<WorkflowSqsSnsOptions>()!;

            aws.ConfigureShareConsumer<AccountDetailsConsumer>(
                context,
                queueName: config.AccountDetails.AccountDetailsQueue,
                topicName: config.AccountDetails.AccountDetailsTopic
            );
        }
    );

    builder.Host.UseDefaultServiceProvider(
        (_, options) =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        }
    );
    builder.Services.AddHostedService<Worker>();

    var app = builder.Build();

    app.UseApiAuthPlugin();

    app.UseHttpsRedirection();

    app.MapDefaultHealthCheckEndpoints();
    
    app.UseShareFastEndpoints(config => config.Endpoints.RoutePrefix = "workflows");

    app.UseShareFastEndpointSwaggerGen("Share.Common.Workflow.Api", "workflows");

    app.UseSerilogRequestLogging();

    await app.ExtractShareApiDetailsAsync("common_workflow");

    Log.Logger.Information("Share.Common.Workflow.Api starting..");

    await app.GenerateApiClientAsync();
    await app.RunAsync();
}
catch (Exception e)
{
    e = e.Demystify();
    Log.Logger.Fatal(e, "Share.Common.Workflow.Api terminated unexpectedly");
}
finally
{
    Log.Logger.Information("Share.Common.Workflow.Api shutting down..");
    await Log.CloseAndFlushAsync();
}
