using Share.Common.Workflow.Api.Client.Models;
using Share.Common.Workflow.Api.Client.Workflows.Applications.Item;
using Share.Common.Workflow.Api.Tests.Setup;
using Shouldly;

namespace Share.Common.Workflow.Api.Tests.Endpoints.Applications;

public class GetApplicationWorkflowTests(ApiApplicationFactory factory)
    : BaseIntegrationTest(factory)
{
    private string? _authToken;
    private readonly Guid _accountId = Guid.NewGuid();

    public override async Task InitializeAsync()
    {
        _authToken = await GetAuthTokenAsync(_accountId);
    }

    [Fact]
    public async Task GetApplicationWorkflow_WithValidRequest_ReturnSuccess()
    {
        // Arrange
        var applicationId = Guid.NewGuid();

        await CreateApplicationTests.CreateApplicationWorkflowAsync(
            ApiClient,
            _authToken!,
            Enums_WorkflowType.DealerLicenseNew,
            applicationId
        );

        // Act
        var response = await ApiClient
            .Workflows.Applications[applicationId]
            .GetWithExceptionHandlingAsync(cfg =>
            {
                cfg.Headers.Add("Authorization", _authToken!);
            });

        // Assert
        response.ShouldNotBeNull();
        response.IsSuccess.ShouldNotBeNull();
        response.IsSuccess.Value.ShouldBeTrue();
        response.Stages.ShouldNotBeNull();
        response.Stages.ShouldNotBeEmpty();
    }
}
