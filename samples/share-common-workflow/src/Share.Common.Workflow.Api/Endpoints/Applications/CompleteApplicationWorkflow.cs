using FastEndpoints;
using FluentValidation;
using Share.Common.Workflow.Core.Application.Services;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Abstractions.Api;

namespace Share.Common.Workflow.Api.Endpoints.Applications;

public class CompleteApplicationWorkflow(ApplicationWorkflowService workflowService)
    : Endpoint<CompleteApplicationWorkflowRequest, BaseEndpointResponse>
{
    public override void Configure()
    {
        Post("/applications/{applicationId:guid}/complete");
        Permissions(Constants.Permissions.Api.Applications.CompleteApplicationWorkflow);
    }

    public override async Task HandleAsync(
        CompleteApplicationWorkflowRequest req,
        CancellationToken ct
    )
    {
        await ValidateAsync(req, ct);
        await workflowService.CompleteApplicationWorkflowAsync(
            req.ApplicationId,
            req.CompletedAt,
            ct
        );

        await SendOkAsync(new BaseEndpointResponse { IsSuccess = true }, ct);
    }

    private async Task ValidateAsync(CompleteApplicationWorkflowRequest req, CancellationToken ct)
    {
        List<string> errors = await workflowService.ValidateCompleteApplicationWorkflowAsync(
            req.ApplicationId,
            ct
        );

        foreach (string error in errors)
        {
            AddError(r => r.ApplicationId, error);
        }

        ThrowIfAnyErrors();
    }
}

public class CompleteApplicationWorkflowRequest
{
    public Guid ApplicationId { get; set; }
    public DateTime CompletedAt { get; set; }
}

public class CompleteApplicationWorkflowRequestValidator
    : Validator<CompleteApplicationWorkflowRequest>
{
    public CompleteApplicationWorkflowRequestValidator()
    {
        RuleFor(r => r.ApplicationId).NotEmpty();
        RuleFor(r => r.CompletedAt).NotEmpty();
    }
}
