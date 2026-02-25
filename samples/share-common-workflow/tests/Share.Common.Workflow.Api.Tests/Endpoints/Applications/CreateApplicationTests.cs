using Share.Common.Workflow.Api.Client;
using Share.Common.Workflow.Api.Client.Models;
using Share.Common.Workflow.Api.Client.Workflows.Item.Applications;
using Share.Common.Workflow.Api.Tests.Setup;
using Shouldly;

namespace Share.Common.Workflow.Api.Tests.Endpoints.Applications;

public class CreateApplicationTests(ApiApplicationFactory factory)
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
    public async Task CreateApplication_WhenValidRequest_ReturnsApplication()
    {
        // Arrange
        var workflowType = Enums_WorkflowType.DealerLicenseNew;

        // Act
        var response = await CreateApplicationWorkflowAsync(
            ApiClient,
            _authToken!,
            workflowType,
            Guid.NewGuid()
        );

        // Assert
        response.ShouldNotBeNull();
        response.IsSuccess.ShouldNotBeNull();
        response.IsSuccess.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateApplication_WhenWorkflowType_NotExists_ReturnsError()
    {
        // Arrange
        var workflowType = Enums_WorkflowType.DealerLicenseRenewal;

        // Act
        var response = await CreateApplicationWorkflowAsync(
            ApiClient,
            _authToken!,
            workflowType,
            Guid.NewGuid()
        );

        // Assert
        response.ShouldNotBeNull();
        response.IsSuccess.ShouldNotBeNull();
        response.IsSuccess.Value.ShouldBeFalse();
        response.Errors.ShouldNotBeNull();
        response.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task CreateApplication_WhenApplicationWorkflow_Exists_ReturnsError()
    {
        // Arrange
        var workflowType = Enums_WorkflowType.DealerLicenseNew;

        var applicationId = Guid.NewGuid();
        await CreateApplicationWorkflowAsync(ApiClient, _authToken!, workflowType, applicationId);
        // Act
        var response = await CreateApplicationWorkflowAsync(
            ApiClient,
            _authToken!,
            workflowType,
            applicationId
        );

        // Assert
        response.ShouldNotBeNull();
        response.IsSuccess.ShouldNotBeNull();
        response.IsSuccess.Value.ShouldBeFalse();
        response.Errors.ShouldNotBeNull();
        response.Errors.ShouldNotBeEmpty();
    }

    public static async Task<BaseEndpointResponse> CreateApplicationWorkflowAsync(
        WorkflowApiClient apiClient,
        string authToken,
        Enums_WorkflowType workflowType,
        Guid applicationId
    )
    {
        var request = new CreateApplicationRequest
        {
            ApplicationId = applicationId,
            ApplicationNumber = applicationId.ToString("N")[..6]
        };

        var response = await apiClient
            .Workflows[workflowType.ToString("G")]
            .Applications.PostWithExceptionHandlingAsync(
                request,
                cfg =>
                {
                    cfg.Headers.Add("Authorization", authToken);
                }
            );

        response.ShouldNotBeNull();

        return response;
    }
}
