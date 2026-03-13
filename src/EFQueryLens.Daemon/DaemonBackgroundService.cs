using Microsoft.Extensions.Hosting;

namespace EFQueryLens.Daemon;

internal sealed class DaemonBackgroundService(
    DaemonHost daemonHost,
    IHostApplicationLifetime applicationLifetime)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await daemonHost.RunAsync(stoppingToken);
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
    }
}
