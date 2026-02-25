using Share.Common.Workflow.Api.Client;
using Share.Common.Workflow.Api.Client.Models;
using Share.Common.Workflow.Api.Client.Workflows.Applications.Item.AssignOfficer;
using Share.Common.Workflow.Api.Tests.Setup;
using Share.Common.Workflow.Core.Domain;
using Shouldly;

namespace Share.Common.Workflow.Api.Tests.Endpoints.Applications;

public class AssignWorkflowStageTest(ApiApplicationFactory factory)
    : BaseIntegrationTest(factory),
        IAsyncLifetime
{
    private string? _authToken;
    private readonly Guid _accountId = Guid.NewGuid();
    private readonly Guid _assignerAccountId = Guid.NewGuid();

    public new async Task InitializeAsync()
    {
        await CreateAccountsIfNotExistAsync([_accountId, _assignerAccountId]);
        _authToken = await GetAuthTokenAsync(_assignerAccountId);
    }

    [Fact]
    public async Task AssignWorkflowStage_WhenValidRequest_ReturnsApplication()
    {
        // Arrange
        var applicationId = Guid.NewGuid();

        await CreateApplicationTests.CreateApplicationWorkflowAsync(
            ApiClient,
            _authToken!,
            Enums_WorkflowType.DealerLicenseNew,
            applicationId
        );

        var body = new AssignWorkflowStageRequest
        {
            Stage = 1,
            Level = 2,
            AssingeeAccountId = _accountId,
            AssigneerAccountId = _assignerAccountId
        };

        // Act
        var response = await AssignWorkflowStageAsync(ApiClient, _authToken!, applicationId, body);
        // Assert
        response.ShouldNotBeNull();
        response.IsSuccess.ShouldNotBeNull();
        response.IsSuccess.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task AssignWorkflowStage_WhenApplicationWorkflow_NotExists_ReturnsError()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        var body = new AssignWorkflowStageRequest
        {
            Stage = 1,
            Level = 1,
            AssingeeAccountId = _accountId,
            AssigneerAccountId = _assignerAccountId
        };

        // Act
        var response = await AssignWorkflowStageAsync(ApiClient, _authToken!, applicationId, body);
        // Assert
        response.ShouldNotBeNull();
        response.IsSuccess.ShouldNotBeNull();
        response.IsSuccess.Value.ShouldBeFalse();
        response.Errors.ShouldNotBeNull();
        response.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task AssignWorkflowStage_WhenLevel_NotExists_ReturnsError()
    {
        // Arrange
        var applicationId = Guid.NewGuid();

        await CreateApplicationTests.CreateApplicationWorkflowAsync(
            ApiClient,
            _authToken!,
            Enums_WorkflowType.DealerLicenseNew,
            applicationId
        );

        var body = new AssignWorkflowStageRequest
        {
            Stage = 1,
            Level = 5,
            AssingeeAccountId = _accountId,
            AssigneerAccountId = _assignerAccountId
        };

        // Act
        var response = await AssignWorkflowStageAsync(ApiClient, _authToken!, applicationId, body);
        // Assert
        response.ShouldNotBeNull();
        response.IsSuccess.ShouldNotBeNull();
        response.IsSuccess.Value.ShouldBeFalse();
        response.Errors.ShouldNotBeNull();
        response.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task AssignWorkflowStage_WhenStage_NotExists_ReturnsError()
    {
        // Arrange
        var applicationId = Guid.NewGuid();

        await CreateApplicationTests.CreateApplicationWorkflowAsync(
            ApiClient,
            _authToken!,
            Enums_WorkflowType.DealerLicenseNew,
            applicationId
        );

        var body = new AssignWorkflowStageRequest
        {
            Stage = 5,
            Level = 1,
            AssingeeAccountId = _accountId,
            AssigneerAccountId = _assignerAccountId
        };

        // Act
        var response = await AssignWorkflowStageAsync(ApiClient, _authToken!, applicationId, body);
        // Assert
        response.ShouldNotBeNull();
        response.IsSuccess.ShouldNotBeNull();
        response.IsSuccess.Value.ShouldBeFalse();
        response.Errors.ShouldNotBeNull();
        response.Errors.ShouldNotBeEmpty();
    }

    public static async Task<BaseEndpointResponse> AssignWorkflowStageAsync(
        WorkflowApiClient apiClient,
        string authToken,
        Guid applicationId,
        AssignWorkflowStageRequest body
    )
    {
        var resp = await apiClient
            .Workflows.Applications[applicationId]
            .AssignOfficer.PostWithExceptionHandlingAsync(
                body,
                cfg =>
                {
                    cfg.Headers.Add("Authorization", authToken);
                }
            );

        resp.ShouldNotBeNull();

        return resp;
    }
}
