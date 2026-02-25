using MassTransit;
using Share.Common.Workflow.Api.Client;
using Share.Common.Workflow.Api.Tests.Helpers;
using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Entities;
using Share.Common.Workflow.Core.Infrastructure.Services;
using Share.IntegrationTest.Abstractions.ServiceFactory;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Xunit.Priority;

namespace Share.Common.Workflow.Api.Tests.Setup;

[CollectionDefinition(nameof(ApiApplicationCollection))]
[TestCaseOrderer(PriorityOrderer.Name, PriorityOrderer.Assembly)]
public class ApiApplicationCollection : ICollectionFixture<ApiApplicationFactory>;

public class ApiApplicationFactory()
    : ShareApiApplicationFactory<ApiTestMarker, WorkflowDbContext>(
        typeof(ApiApplicationCollection),
        "common.workflow.api",
        Constants.Configurations.ConnectionStrings.ApplicationConnectionString
    )
{
    protected override void InjectGitlabServicesSettings(IWebHostBuilder builder)
    {
        builder.UseSetting("Aws:RedisConnectionString", "redis:6379");
        builder.UseSetting("LocalStack:Config:LocalStackHost", "localstack");
        builder.UseSetting("LocalStack:Config:EdgePort", "4566");
        builder.UseSetting("Aws:ServiceURL", "http://localstack:4566");
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddWorkflowApiClient("http://localhost", "xxx", "xxx");
        services.AddMassTransitTestHarness();
    }

    protected override async Task InitiateServiceAsync(IServiceProvider provider)
    {
        var configuration = Services.GetRequiredService<IConfiguration>();
        var authHelper = new AuthHelper(Db, configuration);
        var workflowHelper = new WorkflowHelper(Db, configuration);

        await authHelper.SeedServiceAccountAsync();
        await workflowHelper.SeedWorkflowsAsync();
    }
}
