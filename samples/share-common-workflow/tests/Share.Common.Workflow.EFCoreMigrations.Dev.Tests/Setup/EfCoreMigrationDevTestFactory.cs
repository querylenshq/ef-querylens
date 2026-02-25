using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Infrastructure.Services;
using Share.IntegrationTest.Abstractions.ServiceFactory;
using Share.Lib.EntityFrameworkCore.MySql;
using Xunit.Priority;

namespace Share.Common.Workflow.EFCoreMigrations.Dev.Tests.Setup;

[CollectionDefinition(nameof(EfCoreMigrationDevCollection))]
[TestCaseOrderer(PriorityOrderer.Name, PriorityOrderer.Assembly)]
public class EfCoreMigrationDevCollection : ICollectionFixture<EfCoreMigrationDevTestFactory>;

public class EfCoreMigrationDevTestFactory()
    : ShareWorkerHostServiceFactory<EfCoreMigrationsDevTestMarker, WorkflowDbContext>(
        typeof(EfCoreMigrationDevCollection),
        "common.workflow.ef",
        Constants.Configurations.ConnectionStrings.ApplicationConnectionString
    )
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddEmptyTestDbContext(
            Constants.Configurations.ConnectionStrings.ApplicationConnectionString
        );
    }
}
