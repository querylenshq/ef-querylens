using Microsoft.EntityFrameworkCore;
using Moq;
using Share.Common.Workflow.Api.Tests.Setup;
using Share.Lib.Abstractions.Common.Services;
using Shouldly;

namespace Share.Common.Workflow.Api.Tests;

public class WorkerTests(ApiApplicationFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task ExecuteAsync_Succeeds()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        var hostEnvironmentMock = new Mock<IHostEnvironment>();
        hostEnvironmentMock.Setup(x => x.EnvironmentName).Returns(Environments.Development);

        var workerService = new Worker(
            Configuration,
            hostEnvironmentMock.Object,
            Services.GetRequiredService<IHostApplicationLifetime>(),
            Services,
            Services.GetRequiredService<ILogger<Worker>>()
        );

        // Act
        await workerService.StartAsync(ct);

        await WaitForCompletionAsync(workerService, ct);

        // Assert
        var currentUserOptions = Configuration
            .GetSection(nameof(CurrentUserOptions))
            .Get<CurrentUserOptions>()!;

        bool userExists = await CheckUserExistsAsync(currentUserOptions.ServiceAccountId, ct);

        userExists.ShouldBeTrue();
    }

    private static Task WaitForCompletionAsync(
        BackgroundService workerService,
        CancellationToken ct
    )
    {
        if (workerService.ExecuteTask != null)
        {
            return workerService.ExecuteTask.WaitAsync(ct);
        }

        return Task.CompletedTask;
    }

    private Task<bool> CheckUserExistsAsync(Guid accountId, CancellationToken ct)
    {
        return Db.Accounts.AnyAsync(x => x.AccountId == accountId && x.IsNotDeleted, ct);
    }
}
