using Microsoft.EntityFrameworkCore;
using Share.Common.Workflow.Api.Client;
using Share.Common.Workflow.Api.Client.Models;
using Share.Common.Workflow.Api.Client.Workflows.Applications.Item;
using Share.Common.Workflow.Api.Tests.Setup;
using Shouldly;
using Xunit.Abstractions;

namespace Share.Common.Workflow.Api.Tests.Endpoints.Applications;

public class PostWorkflowStageDecisionTest(ApiApplicationFactory factory)
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
    public async Task PostWorkflowStageDecision_WhenValidRequest_ReturnsApplication()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        await CreateNewDealerLicenseApplicationWorkflowAsync(ApiClient, _authToken!, applicationId);

        await AssignWorkflowStageAsync(
            ApiClient,
            _authToken!,
            _accountId,
            _assignerAccountId,
            applicationId,
            2,
            1
        );

        var body = new PostWorkflowStageDecisionRequest
        {
            AccountId = _accountId,
            Decision = Enums_WorkflowDecision.Approve,
            NextStage = 1,
            NextLevel = 3,
            DecisionDate = DateTimeOffset.Now,
            Remarks = "Verified and approved"
        };

        // Act
        var response = await PostWorkflowStageDecisionAsync(
            ApiClient,
            _authToken!,
            applicationId,
            body
        );
        // Assert
        response.ShouldNotBeNull();
        response.IsSuccess.ShouldNotBeNull();
        response.IsSuccess.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task PostWorkflowStageDecision_WhenApplicationWorkflow_NotExists_ReturnsError()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        var body = new PostWorkflowStageDecisionRequest
        {
            AccountId = _accountId,
            Decision = Enums_WorkflowDecision.Approve,
            NextStage = 1,
            NextLevel = 3,
            DecisionDate = DateTimeOffset.Now,
            Remarks = "Verified and approved"
        };

        // Act
        var response = await PostWorkflowStageDecisionAsync(
            ApiClient,
            _authToken!,
            applicationId,
            body
        );
        // Assert
        response.ShouldNotBeNull();
        response.IsSuccess.ShouldNotBeNull();
        response.IsSuccess.Value.ShouldBeFalse();
        response.Errors.ShouldNotBeNull();
        response.Errors.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(2, 5)]
    public async Task PostWorkflowStageDecision_WhenLevel_NotExists_ReturnsError(
        int level,
        int stage
    )
    {
        // Arrange
        var applicationId = Guid.NewGuid();

        await CreateNewDealerLicenseApplicationWorkflowAsync(ApiClient, _authToken!, applicationId);

        await AssignWorkflowStageAsync(
            ApiClient,
            _authToken!,
            _accountId,
            _assignerAccountId,
            applicationId,
            2,
            1
        );

        var body = new PostWorkflowStageDecisionRequest
        {
            AccountId = _accountId,
            Decision = Enums_WorkflowDecision.Approve,
            NextStage = stage,
            NextLevel = level,
            DecisionDate = DateTimeOffset.Now,
            Remarks = "Verified and approved"
        };

        // Act
        var response = await PostWorkflowStageDecisionAsync(
            ApiClient,
            _authToken!,
            applicationId,
            body
        );
        // Assert
        response.ShouldNotBeNull();
        response.IsSuccess.ShouldNotBeNull();
        response.IsSuccess.Value.ShouldBeFalse();
        response.Errors.ShouldNotBeNull();
        response.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task PostWorkflowStageDecision_WhenStage_NotAssigned_ReturnsError()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        await CreateNewDealerLicenseApplicationWorkflowAsync(ApiClient, _authToken!, applicationId);

        var body = new PostWorkflowStageDecisionRequest
        {
            AccountId = _accountId,
            Decision = Enums_WorkflowDecision.Approve,
            NextStage = 5,
            NextLevel = 2,
            DecisionDate = DateTimeOffset.Now,
            Remarks = "Verified and approved"
        };

        // Act
        var response = await PostWorkflowStageDecisionAsync(
            ApiClient,
            _authToken!,
            applicationId,
            body
        );
        // Assert
        response.ShouldNotBeNull();
        response.IsSuccess.ShouldNotBeNull();
        response.IsSuccess.Value.ShouldBeFalse();
        response.Errors.ShouldNotBeNull();
        response.Errors.ShouldNotBeEmpty();
    }

    public static async Task<BaseEndpointResponse> PostWorkflowStageDecisionAsync(
        WorkflowApiClient apiClient,
        string authToken,
        Guid applicationId,
        PostWorkflowStageDecisionRequest body
    )
    {
        var resp = await apiClient
            .Workflows.Applications[applicationId]
            .PostWithExceptionHandlingAsync(
                body,
                cfg =>
                {
                    cfg.Headers.Add("Authorization", authToken);
                }
            );

        resp.ShouldNotBeNull();

        return resp;
    }

    internal static async Task CreateNewDealerLicenseApplicationWorkflowAsync(
        WorkflowApiClient apiClient,
        string authToken,
        Guid applicationId
    )
    {
        var resp = await CreateApplicationTests.CreateApplicationWorkflowAsync(
            apiClient,
            authToken,
            Enums_WorkflowType.DealerLicenseNew,
            applicationId
        );

        resp.ShouldNotBeNull();
        resp.IsSuccess.ShouldNotBeNull();
        resp.IsSuccess.Value.ShouldBeTrue();
    }

    private static async Task AssignWorkflowStageAsync(
        WorkflowApiClient apiClient,
        string authToken,
        Guid accountId,
        Guid assignerAccountId,
        Guid applicationId,
        int level,
        int stage
    )
    {
        var body = new AssignWorkflowStageRequest
        {
            Stage = stage,
            Level = level,
            AssingeeAccountId = accountId,
            AssigneerAccountId = assignerAccountId
        };
        var resp = await AssignWorkflowStageTest.AssignWorkflowStageAsync(
            apiClient,
            authToken,
            applicationId,
            body
        );
        resp.ShouldNotBeNull();
        resp.IsSuccess.ShouldNotBeNull();
        resp.IsSuccess.Value.ShouldBeTrue();
    }
}
