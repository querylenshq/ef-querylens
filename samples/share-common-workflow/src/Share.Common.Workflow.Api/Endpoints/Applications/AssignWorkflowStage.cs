using FastEndpoints;
using FluentValidation;
using Share.Common.Workflow.Core.Application.Services;
using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Resources;
using Share.Lib.Abstractions.Api;

namespace Share.Common.Workflow.Api.Endpoints.Applications;

public class AssignWorkflowStage(ApplicationWorkflowService workflowService)
    : Endpoint<AssignWorkflowStageRequest, BaseEndpointResponse>
{
    public override void Configure()
    {
        Post("/applications/{applicationId:guid}/assign-officer");
        Permissions(Constants.Permissions.Api.Applications.AssignWorkflowStage);
    }

    public override async Task HandleAsync(AssignWorkflowStageRequest req, CancellationToken ct)
    {
        await ValidateAsync(req, ct);
        await workflowService.AssignWorkflowStageAsync(
            req.ApplicationId,
            req.Stage,
            req.Level,
            req.AssingeeAccountId,
            req.AssigneerAccountId,
            req.Remarks,
            ct
        );

        await SendOkAsync(new BaseEndpointResponse { IsSuccess = true }, ct);
    }

    private async Task ValidateAsync(AssignWorkflowStageRequest req, CancellationToken ct)
    {
        var workflow = await workflowService.GetAppWorkflowByIdAsync(
            req.ApplicationId,
            appWorkflow => new
            {
                appWorkflow.Status,
                Levels = appWorkflow
                    .Levels.Select(l => new
                    {
                        l.WorkflowLevel.Level,
                        Stages = l.Stages.Select(s => s.Stage.Stage).ToList()
                    })
                    .ToList()
            },
            ct
        );

        if (workflow is not { Status: Enums.WorkflowStatus.InProgress })
        {
            AddError(r => r.ApplicationId, WorkflowValidation.ActiveWorkflowNotExists);
        }
        else
        {
            if (workflow.Levels.TrueForAll(a => a.Level != req.Level))
            {
                AddError(r => r.Level, WorkflowValidation.AppWorkflowLevelNotExists);
            }
            else if (
                workflow.Levels.Exists(a =>
                    a.Level == req.Level && a.Stages.TrueForAll(s => s != req.Stage)
                )
            )
            {
                AddError(r => r.Stage, WorkflowValidation.AppWorkflowLevelStageNotExists);
            }
        }

        ThrowIfAnyErrors();
    }
}

public class AssignWorkflowStageRequestValidator : Validator<AssignWorkflowStageRequest>
{
    public AssignWorkflowStageRequestValidator()
    {
        RuleFor(r => r.ApplicationId).NotEmpty();
        RuleFor(r => r.Stage).NotEmpty();
        RuleFor(r => r.Level).NotEmpty();
    }
}
