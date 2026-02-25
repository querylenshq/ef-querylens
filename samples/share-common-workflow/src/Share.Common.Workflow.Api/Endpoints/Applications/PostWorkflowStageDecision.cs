using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Share.Common.Workflow.Core.Application.Services;
using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Resources;
using Share.Lib.Abstractions.Api;

namespace Share.Common.Workflow.Api.Endpoints.Applications;

public class PostWorkflowStageDecision(ApplicationWorkflowService workflowService)
    : Endpoint<PostWorkflowStageDecisionRequest, BaseEndpointResponse>
{
    public override void Configure()
    {
        Post("/applications/{applicationId:guid}");
        Permissions(Constants.Permissions.Api.Applications.PostWorkflowStageDecision);
    }

    public override async Task HandleAsync(
        PostWorkflowStageDecisionRequest req,
        CancellationToken ct
    )
    {
        await ValidateAsync(req, ct);

        await workflowService.PostWorkflowStageDecisionAsync(req, ct);

        await SendOkAsync(new BaseEndpointResponse { IsSuccess = true }, ct);
    }

    private async Task ValidateAsync(PostWorkflowStageDecisionRequest req, CancellationToken ct)
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
                        l.IsActive,
                        Stages = l
                            .Stages.Select(s => new
                            {
                                s.Stage.Stage,
                                StagePrivileges = s
                                    .Stage.Privileges.Where(w => w.IsNotDeleted)
                                    .Select(p => new
                                    {
                                        p.PrivilegeRequirementType,
                                        p.PrivilegeType
                                    })
                                    .ToList(),
                                PrivilegeActions = s
                                    .PrivilegeActions.Select(a => new
                                    {
                                        a.ConditionType,
                                        IsComplete = a.CompletedAt != null
                                    })
                                    .ToList(),
                                s.IsActive,
                                s.Stage.IsFinal,
                                IsPending = s.Actions.Any(w =>
                                    w.Decision == Enums.WorkflowDecision.Pending
                                )
                            })
                            .ToList()
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
            if (req.IsFinalStage)
            {
                var lastLevel = workflow.Levels.Max(a => a.Level);
                var activeLevel = workflow.Levels.SingleOrDefault(a => a.IsActive);
                var lastStage = workflow
                    .Levels.Single(a => a.Level == lastLevel)
                    .Stages.Max(a => a.Stage);
                var activeStage = activeLevel?.Stages.SingleOrDefault(a =>
                    a.IsActive && a.IsPending
                );

                if (
                    activeLevel == null
                    || activeLevel.Level != lastLevel
                    || activeStage == null
                    || activeStage.Stage != lastStage
                )
                {
                    AddError(r => r.Decision, WorkflowValidation.AppWorkflowLevelStageNotExists);
                }
            }
            else
            {
                if (
                    !workflow.Levels.Exists(r =>
                        r.IsActive && r.Stages.Exists(s => s.IsActive && s.IsPending)
                    )
                )
                {
                    AddError(r => r.Decision, WorkflowValidation.AppWorkflowLevelStageNotExists);
                }

                if (
                    req.Decision != Enums.WorkflowDecision.InstantApprove
                    && req.Decision != Enums.WorkflowDecision.InstantReject
                )
                {
                    if (workflow.Levels.TrueForAll(a => a.Level != req.NextLevel))
                    {
                        AddError(
                            r => r.NextLevel,
                            WorkflowValidation.AppWorkflowLevelStageNotExists
                        );
                    }
                    else if (
                        workflow.Levels.Exists(a =>
                            a.Level == req.NextLevel
                            && a.Stages.TrueForAll(s => s.Stage != req.NextStage)
                        )
                    )
                    {
                        AddError(
                            r => r.NextStage,
                            WorkflowValidation.AppWorkflowLevelStageNotExists
                        );
                    }
                }
            }

            var isPrivilegeExist = workflow
                .Levels.Where(w => w.IsActive)
                .SelectMany(s => s.Stages)
                .Where(w => w.IsActive && w.IsPending)
                .SelectMany(s => s.StagePrivileges)
                .Any(s =>
                    s.PrivilegeType
                    == Constants.WorkflowConstants.DecisionToPrivilegeTypeMap[req.Decision]
                );

            if (!isPrivilegeExist)
            {
                AddError(r => r.Decision, WorkflowValidation.AppWorkflowStagePrivilegeNotExist);
            }

            List<Enums.WorkflowPrivilegeRequirementType> requiredConditionTypes =
            [
                Enums.WorkflowPrivilegeRequirementType.RequiredIfInitiated,
                Enums.WorkflowPrivilegeRequirementType.RequiredToCompleteStage
            ];
            List<Enums.WorkflowPrivilegeType> privilegeTypes =
            [
                Enums.WorkflowPrivilegeType.Approve
            ];

            var stagePrevPrivilegeTypes = workflow
                .Levels.Where(w => w.IsActive)
                .SelectMany(l =>
                    l.Stages.Where(w => w.IsActive && w.IsPending)
                        .SelectMany(s =>
                            s.StagePrivileges.Where(w =>
                                !privilegeTypes.Contains(w.PrivilegeType)
                                && requiredConditionTypes.Contains(w.PrivilegeRequirementType)
                            )
                        )
                        .Select(s => s.PrivilegeType)
                )
                .ToList();

            var pendingActions = workflow
                .Levels.Where(w => w.IsActive)
                .SelectMany(l =>
                    l.Stages.Where(w => w.IsActive && w.IsPending)
                        .SelectMany(s =>
                            s.PrivilegeActions.Where(w =>
                                    !w.IsComplete
                                    && stagePrevPrivilegeTypes.Contains(w.ConditionType)
                                )
                                .Select(c => c.ConditionType)
                        )
                )
                .ToList();

            if (pendingActions.Count > 0)
            {
                AddError(
                    r => r.Decision,
                    string.Format(
                        WorkflowValidation.AppWorkflowPendingAction,
                        string.Join(", ", pendingActions)
                    )
                );
            }
        }

        ThrowIfAnyErrors();
    }
}

public class PostWorkflowStageDecisionRequest
{
    public Guid ApplicationId { get; set; }

    public Enums.WorkflowDecision Decision { get; set; }

    public DateTime DecisionDate { get; set; }

    public string? Remarks { get; set; }

    public int? NextLevel { get; set; }

    public int? NextStage { get; set; }

    public bool IsFinalStage { get; set; }
    public Guid? AccountId { get; set; }

    public static implicit operator PostWorkflowStageDecisionTaskModel(
        PostWorkflowStageDecisionRequest req
    ) => req.ToTaskModel();
}

public class PostWorkflowStageDecisionRequestValidator : Validator<PostWorkflowStageDecisionRequest>
{
    public PostWorkflowStageDecisionRequestValidator()
    {
        RuleFor(r => r.ApplicationId).NotEmpty();
        RuleFor(r => r.Decision).IsInEnum();
        RuleFor(r => r.DecisionDate).NotEmpty();

        When(
            r =>
                !r.IsFinalStage
                && r.Decision != Enums.WorkflowDecision.InstantReject
                && r.Decision != Enums.WorkflowDecision.InstantApprove,
            () =>
            {
                RuleFor(r => r.NextLevel).NotEmpty();
                RuleFor(r => r.NextStage).NotEmpty();
            }
        );
    }
}
