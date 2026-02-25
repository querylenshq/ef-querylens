using MassTransit;
using MassTransit.Testing;

namespace Share.Common.Workflow.Api.Tests.Helpers;

internal class BusControlHelper
{
    public BusControlHelper(IServiceProvider services, ITestHarness testHarness)
    {
        Services = services;
        TestHarness = testHarness;
    }

    internal readonly IServiceProvider Services;
    internal readonly ITestHarness TestHarness;

    public async Task WaitForStartAsync(CancellationToken ct)
    {
        var bus = Services.GetRequiredService<IBusControl>();

        while (true)
        {
            var health = bus.CheckHealth();
            if (health.Status == BusHealthStatus.Healthy)
            {
                return;
            }

            var mt = Services
                .GetServices<IHostedService>()
                .OfType<MassTransitHostedService>()
                .Single();

            await mt.StartAsync(ct);
        }
    }

    public async Task WaitForConsumptionWithTimeoutAsync<TMessageContract>(
        TimeSpan timeout,
        FilterDelegate<IReceivedMessage<TMessageContract>>? filter = null
    )
        where TMessageContract : class
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            filter = filter ?? (x => true);
            while (!await TestHarness.Consumed.Any(filter, cts.Token))
            {
                await Task.Delay(100, cts.Token); // Pass the token to Task.Delay
            }
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("Operation timed out.");
        }
    }
}
