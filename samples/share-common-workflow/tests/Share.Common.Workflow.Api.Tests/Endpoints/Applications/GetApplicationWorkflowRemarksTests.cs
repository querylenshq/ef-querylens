using Share.Common.Workflow.Api.Client;
using Share.Common.Workflow.Api.Client.Models;
using Share.Common.Workflow.Api.Client.Workflows.Applications.Item.Remarks;
using Share.Common.Workflow.Api.Tests.Setup;
using Shouldly;

namespace Share.Common.Workflow.Api.Tests.Endpoints.Applications;

public class GetApplicationWorkflowRemarksTests(ApiApplicationFactory factory)
    : BaseIntegrationTest(factory)
{
    private string? _authToken;
    private readonly Guid _accountId = Guid.NewGuid();
    private readonly Guid _assignerAccountId = Guid.NewGuid();

    public override async Task InitializeAsync()
    {
        await CreateAccountsIfNotExistAsync([_accountId, _assignerAccountId]);
        _authToken = await GetAuthTokenAsync(_assignerAccountId);
    }

    [Fact]
    public async Task GetApplicationWorkflowRemarks_WithValidRequest_ReturnSuccess()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        await PostWorkflowStageDecisionTest.CreateNewDealerLicenseApplicationWorkflowAsync(
            ApiClient,
            _authToken!,
            applicationId
        );

        await AssignWorkflowStageAsync(
            ApiClient,
            _authToken!,
            _accountId,
            _assignerAccountId,
            applicationId
        );

        // Act
        var response = await ApiClient
            .Workflows.Applications[applicationId]
            .Remarks.GetWithExceptionHandlingAsync(cfg =>
            {
                cfg.Headers.Add("Authorization", _authToken!);
            });

        // Assert
        response.ShouldNotBeNull();
        response.IsSuccess.ShouldNotBeNull();
        response.IsSuccess.Value.ShouldBeTrue();
        response.Stages.ShouldNotBeEmpty();
    }

    private static async Task AssignWorkflowStageAsync(
        WorkflowApiClient apiClient,
        string authToken,
        Guid accountId,
        Guid assignerAccountId,
        Guid applicationId
    )
    {
        var body = new AssignWorkflowStageRequest
        {
            Stage = 1,
            Level = 2,
            AssingeeAccountId = accountId,
            AssigneerAccountId = assignerAccountId,
            Remarks = "Test Remarks"
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
