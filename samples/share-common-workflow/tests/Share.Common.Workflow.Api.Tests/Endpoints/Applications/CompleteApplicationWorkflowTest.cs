using Share.Common.Workflow.Api.Client;
using Share.Common.Workflow.Api.Client.Models;
using Share.Common.Workflow.Api.Client.Workflows.Applications.Item.Complete;
using Share.Common.Workflow.Api.Tests.Setup;
using Shouldly;

namespace Share.Common.Workflow.Api.Tests.Endpoints.Applications;

public class CompleteApplicationWorkflowTest(ApiApplicationFactory factory)
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
    public async Task CompleteApplicationWorkflow_WhenValidRequest_ReturnsApplication()
    {
        // Arrange
        var applicationId = Guid.NewGuid();

        await PostWorkflowStageDecisionTest.CreateNewDealerLicenseApplicationWorkflowAsync(
            ApiClient,
            _authToken!,
            applicationId
        );

        await AssignAndSubmitDecisionAsync(
            applicationId,
            2,
            1,
            false,
            Enums_WorkflowDecision.Approve
        );
        await AssignAndSubmitDecisionAsync(
            applicationId,
            3,
            1,
            true,
            Enums_WorkflowDecision.Approve
        );

        var body = new CompleteApplicationWorkflowRequest { CompletedAt = DateTimeOffset.Now };

        // Act
        var response = await CompleteApplicationWorkflowAsync(
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

    private async Task AssignAndSubmitDecisionAsync(
        Guid applicationId,
        int level,
        int stage,
        bool isFinalStage,
        Enums_WorkflowDecision decision
    )
    {
        var assignResp = await AssignWorkflowStageTest.AssignWorkflowStageAsync(
            ApiClient,
            _authToken!,
            applicationId,
            new AssignWorkflowStageRequest
            {
                AssingeeAccountId = _accountId,
                AssigneerAccountId = _assignerAccountId,
                Level = level,
                Stage = stage
            }
        );

        assignResp.ShouldNotBeNull();
        assignResp.IsSuccess.ShouldNotBeNull();
        assignResp.IsSuccess.Value.ShouldBeTrue();

        var decisionResp = await PostWorkflowStageDecisionTest.PostWorkflowStageDecisionAsync(
            ApiClient,
            _authToken!,
            applicationId,
            new PostWorkflowStageDecisionRequest
            {
                AccountId = _accountId,
                Decision = decision,
                NextStage = stage,
                NextLevel = (level + 1),
                DecisionDate = DateTimeOffset.Now,
                IsFinalStage = isFinalStage,
                Remarks = "Verified and approved"
            }
        );

        decisionResp.ShouldNotBeNull();
        decisionResp.IsSuccess.ShouldNotBeNull();
        decisionResp.IsSuccess.Value.ShouldBeTrue();
    }

    private static async Task<BaseEndpointResponse> CompleteApplicationWorkflowAsync(
        WorkflowApiClient apiClient,
        string authToken,
        Guid applicationId,
        CompleteApplicationWorkflowRequest? body = null
    )
    {
        body ??= new CompleteApplicationWorkflowRequest { CompletedAt = DateTimeOffset.Now };

        var resp = await apiClient
            .Workflows.Applications[applicationId]
            .Complete.PostWithExceptionHandlingAsync(
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
