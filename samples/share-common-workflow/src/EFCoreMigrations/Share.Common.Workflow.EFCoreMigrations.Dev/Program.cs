using System.Diagnostics;
using Serilog;
using Share.Common.Workflow.Core.Infrastructure.Interfaces;
using Share.Common.Workflow.Core.Infrastructure.Services;
using Share.Common.Workflow.EFCoreMigrations.Dev;
using Share.Lib.Abstractions.Common;
using Share.Lib.Abstractions.Common.Extensions;
using Share.Lib.Abstractions.Serilog;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore.MySql;

ConfigureShareSerilog.CreateBootstrapLogger();
var exitCode = 0;
try
{
    var builder = Host.CreateApplicationBuilder(args);

    var services = builder.Services;

    services.AddShareSerilog(preserveStaticLogger: true);
    services.AddShareCommonCoreWorkerServiceServices();

    services.AddShareMySqlDbContext<IWorkflowDbContext, WorkflowDbContext, Account>(
        Share
            .Common
            .Workflow
            .Core
            .Domain
            .Constants
            .Configurations
            .ConnectionStrings
            .ApplicationConnectionString,
        optionsBuilder =>
        {
            optionsBuilder.EnableDetailedErrors();
            optionsBuilder.EnableSensitiveDataLogging();
        },
        optionsBuilder =>
            optionsBuilder.MigrationsAssembly("Share.Common.Workflow.EFCoreMigrations.Dev")
    );

    builder.Services.AddHostedService<Worker>(); // TODO
    //builder.Services.AddHostedService<HSAMED7115Worker>(); // TODO: replace again with the correct Worker

    var host = builder.Build();

    Log.Logger.Information("Application starting up...");

    if (!builder.Environment.IsTesting())
    {
        await host.RunAsync();
    }
}
catch (Exception e)
{
    if (e is HostAbortedException && args.Contains("--applicationName"))
    {
        // This is done to handle EF migrations related activity in the process.
        // For more information, refer to the following link: https://github.com/dotnet/efcore/issues/29809#issuecomment-1345132260
        Log.Logger.Information("Skipping Exception, ef migrations related activity in process..");
    }
    else
    {
        e = e.Demystify();
        Log.Logger.Fatal(e, "An unhandled exception occurred during the execution of the service.");
    }
    exitCode = 1;
}
finally
{
    Log.Logger.Information("Application shutting down...");
    await Log.CloseAndFlushAsync();
}
return exitCode;
