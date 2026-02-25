using Share.Common.Workflow.Core.Infrastructure.Services;
using Share.IntegrationTest.Abstractions.BaseIntegrationTest;

namespace Share.Common.Workflow.EFCoreMigrations.Dev.Tests.Setup;

[Collection(nameof(EfCoreMigrationDevCollection))]
public class BaseIntegrationTest(EfCoreMigrationDevTestFactory factory)
    : ShareWorkerBaseIntegrationTest<
        EfCoreMigrationDevTestFactory,
        EfCoreMigrationsDevTestMarker,
        WorkflowDbContext,
        Worker
    >(factory);
