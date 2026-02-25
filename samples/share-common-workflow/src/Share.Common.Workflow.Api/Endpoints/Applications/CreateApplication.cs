using FastEndpoints;
using FluentValidation;
using Share.Common.Workflow.Core.Application.Services;
using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Resources;
using Share.Lib.Abstractions.Api;

namespace Share.Common.Workflow.Api.Endpoints.Applications;

public class CreateApplication(
    WorkflowService workflowService,
    ApplicationWorkflowService applicationWorkflowService
) : Endpoint<CreateApplicationRequest, BaseEndpointResponse>
{
    public override void Configure()
    {
        Post("/{workflowType:alpha}/applications");
        Permissions(Constants.Permissions.Api.Applications.CreateApplication);
    }

    public override async Task HandleAsync(CreateApplicationRequest req, CancellationToken ct)
    {
        var workflowId = await ValidateAsync(req, ct);

        if (Constants.WorkflowConstants.CnWorkflowTypes.Contains(req.WorkflowType))
        {
            await applicationWorkflowService.CreateApplicationWorkflowAndReplaceIfExistsAsync(
                req.ApplicationId,
                workflowId,
                ct
            );
        }
        else
        {
            await applicationWorkflowService.CreateApplicationWorkflowAsync(
                req.ApplicationId,
                workflowId,
                ct
            );
        }

        await SendOkAsync(new BaseEndpointResponse { IsSuccess = true }, ct);
    }

    private async Task<Guid> ValidateAsync(CreateApplicationRequest req, CancellationToken ct)
    {
        var workflowId = await workflowService.GetWorkflowByTypeAsync(
            req.WorkflowType,
            w => w.WorkflowId,
            ct
        );

        if (workflowId == Guid.Empty)
        {
            AddError(r => r.WorkflowType, WorkflowValidation.WorkflowNotFoundForType);
        }

        var workflow = await applicationWorkflowService.GetAppWorkflowByIdAsync(
            req.ApplicationId,
            appWorkflow => new { appWorkflow.Status, appWorkflow.Workflow.WorkflowType },
            ct
        );

        if (
            workflow is { Status: Enums.WorkflowStatus.InProgress }
            && !Constants.WorkflowConstants.CnWorkflowTypes.Contains(workflow.WorkflowType)
        )
        {
            AddError(r => r.ApplicationId, WorkflowValidation.ApplicationWorkflowExists);
        }

        ThrowIfAnyErrors();

        return workflowId;
    }
}

public class CreateApplicationRequest
{
    public Enums.WorkflowType WorkflowType { get; set; }

    public Guid ApplicationId { get; set; }

    public string ApplicationNumber { get; set; } = default!;
}

public class CreateApplicationRequestValidator : Validator<CreateApplicationRequest>
{
    public CreateApplicationRequestValidator()
    {
        RuleFor(r => r.ApplicationId).NotEmpty();
        RuleFor(r => r.ApplicationNumber).NotEmpty();
        RuleFor(r => r.WorkflowType).IsInEnum();
    }
}
