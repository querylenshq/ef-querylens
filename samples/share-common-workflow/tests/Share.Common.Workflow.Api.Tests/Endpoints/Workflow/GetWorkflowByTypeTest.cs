using Share.Common.Workflow.Api.Client.Models;
using Share.Common.Workflow.Api.Client.Workflows.Item;
using Share.Common.Workflow.Api.Tests.Setup;
using Shouldly;

namespace Share.Common.Workflow.Api.Tests.Endpoints.Workflow;

public class GetWorkflowByTypeTest(ApiApplicationFactory factory)
    : BaseIntegrationTest(factory),
        IAsyncLifetime
{
    private string? _authToken;
    private readonly Guid _accountId = Guid.NewGuid();

    public new async Task InitializeAsync()
    {
        _authToken = await GetAuthTokenAsync(_accountId);
    }

    [Fact]
    public async Task GetWorkflowByType_WhenWorkflowTypeExists_ReturnsWorkflow()
    {
        // Arrange
        var workflowType = Enums_WorkflowType.DealerLicenseNew;
        var workflowTypeStr = workflowType.ToString("G");
        // Act
        var response = await ApiClient
            .Workflows[workflowTypeStr]
            .GetWithExceptionHandlingAsync(cfg =>
            {
                cfg.Headers.Add("Authorization", _authToken!);
            });
        // Assert
        response.ShouldNotBeNull();
        response.IsSuccess.ShouldNotBeNull();
        response.IsSuccess.Value.ShouldBeTrue();

        response.Workflow.ShouldNotBeNull();
        response.Workflow.WorkflowType.ShouldNotBeNull();
        response.Workflow.WorkflowType.ShouldBe(workflowType);
        response.Workflow.Levels.ShouldNotBeNull();
        response.Workflow.Levels.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GetWorkflowByType_WhenWorkflowType_NotExists_ReturnsError()
    {
        // Arrange
        var workflowType = Enums_WorkflowType.DealerLicenseRenewal;
        var workflowTypeStr = workflowType.ToString("G");
        // Act
        var response = await ApiClient
            .Workflows[workflowTypeStr]
            .GetWithExceptionHandlingAsync(cfg =>
            {
                cfg.Headers.Add("Authorization", _authToken!);
            });
        // Assert
        response.ShouldNotBeNull();
        response.IsSuccess.ShouldNotBeNull();
        response.IsSuccess.Value.ShouldBeFalse();
        response.Errors.ShouldNotBeNull();
        response.Errors.ShouldNotBeEmpty();

        response.Workflow.ShouldBeNull();
    }
}
