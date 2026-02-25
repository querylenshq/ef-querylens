using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Share.Common.Workflow.EFCoreMigrations.Dev.Tests.Setup;
using Share.Lib.EntityFrameworkCore.MySql;
using Shouldly;
using Xunit.Priority;

namespace Share.Common.Workflow.EFCoreMigrations.Dev.Tests;

public class WorkerServiceTest(EfCoreMigrationDevTestFactory factory) : BaseIntegrationTest(factory)
{
    [Fact, Priority(0)]
    public async Task WorkerService_ShouldRunMigrations()
    {
        // Arrange
        var ct = new CancellationTokenSource();
        var minimal = Services.GetEmptyTestDbContext();
        await minimal.Database.EnsureDeletedAsync(ct.Token);
        await minimal.Database.EnsureCreatedAsync(ct.Token);
        var before = (
            await Db.Database.GetPendingMigrationsAsync(cancellationToken: ct.Token)
        ).ToList();

        // Act
        await WorkerService.StartAsync(ct.Token);

        await WaitForCompletion(ct.Token);

        var after = (
            await Db.Database.GetAppliedMigrationsAsync(cancellationToken: ct.Token)
        ).ToList();

        // Assert
        Assert.NotNull(WorkerService.ExecuteTask);
        WorkerService.ExecuteTask.Status.ShouldBeEquivalentTo(TaskStatus.RanToCompletion);
        after.ShouldBeEquivalentTo(before);
        Assert.NotEmpty(after);
        ct.Dispose();
    }

    [Fact, Priority(1)]
    public async Task WorkerService_Should_Crash_When_MigrationsAre_Not_Synced()
    {
        // Arrange
        var ct = new CancellationTokenSource();

        await Db.Database.EnsureDeletedAsync(ct.Token);
        await Db.Database.EnsureCreatedAsync(ct.Token);

        //Act
        await WorkerService.StartAsync(ct.Token);
        await Assert.ThrowsAsync<MySqlException>(() => WaitForCompletion(ct.Token));
        var migrations = (
            await Db.Database.GetPendingMigrationsAsync(cancellationToken: ct.Token)
        ).ToList();
        migrations.ShouldNotBeEmpty();

        ct.Dispose();
    }
}
