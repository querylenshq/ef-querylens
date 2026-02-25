using Share.Lib.Abstractions.Api;
using Share.Lib.Abstractions.Common.Extensions;
using Share.Lib.Abstractions.Common.Services;
using Share.Lib.Bootstrap.Api.Core.Application.Interfaces;

namespace Share.Common.Workflow.Api;

public class Worker(
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    IHostApplicationLifetime applicationLifetime,
    IServiceProvider serviceProvider,
    ILogger<Worker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting Worker {Time:G}", DateTime.Now);
        if (!hostEnvironment.IsTesting())
        {
            var scope = serviceProvider.CreateScope();

            var service = scope.ServiceProvider.GetRequiredService<IAccountService>();
            var opts = configuration
                .GetSection(nameof(CurrentUserOptions))
                .Get<CurrentUserOptions>()!;

            await service.SetAccountInformationAsync(
                opts.ServiceAccountId,
                opts.ServiceName,
                new Dictionary<string, IEnumerable<string>>
                {
                    { ShareHttpParams.CommonClaims.Role, ["APP"] }
                },
                applicationLifetime.ApplicationStopping
            );
        }

        logger.LogInformation("Completed Worker {Time:G}", DateTime.Now);
    }
}
