using FastEndpoints;
using Share.Common.Workflow.Api.Services;
using Share.Common.Workflow.Core.Application.Services;
using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Domain.Extensions;
using Share.Lib.Abstractions.Api;

namespace Share.Common.Workflow.Api.Endpoints.Applications;

public class GetApplicationWorkflow(
    ApplicationWorkflowService workflowService,
    WorkflowAccountService workflowAccountService,
    IMedicsCurrentUser currentUser
) : Endpoint<GetApplicationWorkflowRequest, GetApplicationWorkflowResponse>
{
    public override void Configure()
    {
        Get("/applications/{applicationId:guid}");
        Permissions(Constants.Permissions.Api.Applications.GetApplicationWorkflow);
    }

    public override async Task HandleAsync(GetApplicationWorkflowRequest req, CancellationToken ct)
    {
        var currentUserRoles = await workflowAccountService.GetOfficerRolesAsync(
            currentUser.UserAccountId,
            ct
        );

        var workflow = await workflowService.GetAppWorkflowByIdAsync(
            req.ApplicationId,
            workflow => new
            {
                workflow.Status,
                workflow.Workflow.WorkflowType,
                Stages = workflow
                    .Levels.Where(w => w.IsNotDeleted)
                    .SelectMany(l => l.Stages.Where(w => w.IsNotDeleted))
                    .Select(s => new
                    {
                        StageName = s.Stage.Name,
                        s.Stage.StageIdentifier,
                        s.Stage.Stage,
                        s.IsActive,
                        s.AppWorkflowLevel.WorkflowLevel.Level,
                        s.AppWorkflowLevel.WorkflowLevel.WorkflowRole,
                        PrivilegeTypes = s
                            .Stage.Privileges.Where(p => p.IsNotDeleted)
                            .Select(p => p.PrivilegeType)
                    })
                    .OrderBy(o => o.Level)
                    .ThenBy(o => o.Stage)
                    .ToList(),
            },
            ct
        );

        if (workflow == null)
        {
            await SendOkAsync(new GetApplicationWorkflowResponse { IsSuccess = false }, ct);
            return;
        }

        var stages = workflow.Stages;

#pragma warning disable S3358 // Ternary operators should not be nested
        var actions = await workflowService.GetAppWorkflowByIdAsync(
            req.ApplicationId,
            workflow =>
                workflow
                    .Levels.Where(w => w.IsNotDeleted)
                    .SelectMany(l => l.Stages.Where(w => w.IsNotDeleted))
                    .SelectMany(s =>
                        s.Actions.Where(w => w.IsNotDeleted)
                            .OrderByDescending(a =>
                                a.ApplicationCreatedAtValue != null
                                    ? a.ApplicationCreatedAtValue
                                    : a.CreatedAt
                            )
                            .ThenBy(a =>
                                a.Decision == Enums.WorkflowDecision.Pending
                                    ? 0
                                    : (
                                        a.Decision == Enums.WorkflowDecision.UnTag
                                            ? 2
                                            : (a.Decision == Enums.WorkflowDecision.Assign ? 3 : 1)
                                    )
                            )
                            .Take(1)
                            .Where(w =>
                                w.Decision != Enums.WorkflowDecision.UnTag
                                && w.Decision != Enums.WorkflowDecision.Assign
                            )
                    )
                    .Select(a => new
                    {
                        a.CreatedAt,
                        a.Decision,
                        a.DecisionMadeAt,
                        a.AppWorkflowLevelStage.Stage.Stage,
                        a.AppWorkflowLevelStage.AppWorkflowLevel.WorkflowLevel.Level,
                        SupportingOfficer = a.AppOfficer.Officer.Name,
                        OfficerId = a.AppOfficer.Officer.AccountId,
                    })
                    .ToList(),
            ct
        );
#pragma warning restore S3358 // Ternary operators should not be nested

        var res = new GetApplicationWorkflowResponse
        {
            WorkflowType = workflow.WorkflowType,
            IsSuccess = true
        };
        foreach (var stage in stages)
        {
            var acts =
                actions
                    ?.Where(w => w.Stage == stage.Stage && w.Level == stage.Level)
                    .OrderBy(o => o.CreatedAt)
                    .ToList() ?? [];

            var stageId = $"ID{stage.Level}.{stage.Stage}";
            if (acts.Count == 0)
            {
                res.Stages.Add(
                    new GetApplicationWorkflowResponse.ApplicationWorkflowStageItem
                    {
                        StageId = stageId,
                        StageName = stage.StageName,
                        IsActive = stage.IsActive,
                        CanAssignToMe =
                            stage.IsActive
                            && currentUserRoles.Contains(
                                (stage.WorkflowRole.ToMedicsRoleType(), workflow.WorkflowType)
                            ),
                        CanReject = false
                    }
                );
            }

            foreach (var act in acts)
            {
                res.Stages.Add(
                    new GetApplicationWorkflowResponse.ApplicationWorkflowStageItem
                    {
                        StageId = stageId,
                        IsActive = stage.IsActive,
                        StageName = stage.StageName,
                        StageIdentifier = stage.StageIdentifier,
                        StartedAt = act.CreatedAt,
                        CompletedAt = act.DecisionMadeAt,
                        Decision = act.Decision,
                        SupportingOfficer = act.SupportingOfficer,
                        SupportingOfficerId = act.OfficerId,
                        CanAssignToMe =
                            stage.IsActive
                            && act.OfficerId != currentUser.UserAccountId
                            && currentUserRoles.Contains(
                                (stage.WorkflowRole.ToMedicsRoleType(), workflow.WorkflowType)
                            ),
                        CanReject =
                            stage.IsActive
                            && (
                                stage.PrivilegeTypes.Contains(Enums.WorkflowPrivilegeType.Reject)
                                || stage.PrivilegeTypes.Contains(
                                    Enums.WorkflowPrivilegeType.InstantReject
                                )
                            )
                            && act.Decision == Enums.WorkflowDecision.Pending
                            && act.OfficerId == currentUser.UserAccountId
                    }
                );
            }
        }

        if (
            !res.Stages.Exists(s => s.IsActive)
            && workflow.Status == Enums.WorkflowStatus.InProgress
            && stages.Count > 0
            && res.Stages.Count > 0
        )
        {
            var firstStage = stages[0];
            var responseFirstStage = res.Stages[0];

            responseFirstStage.IsActive = true;
            responseFirstStage.CanAssignToMe = currentUserRoles.Contains(
                (firstStage.WorkflowRole.ToMedicsRoleType(), workflow.WorkflowType)
            );
        }

        await SendOkAsync(res, ct);
    }
}

public class GetApplicationWorkflowRequest
{
    public Guid ApplicationId { get; set; }
}

public class GetApplicationWorkflowResponse : BaseEndpointResponse
{
    public Enums.WorkflowType WorkflowType { get; set; }

    public List<ApplicationWorkflowStageItem> Stages { get; set; } = [];

    public class ApplicationWorkflowStageItem
    {
        public string StageId { get; set; } = default!;

        public string StageName { get; set; } = default!;

        public Enums.WorkflowStageIdentifier StageIdentifier { get; set; }

        public DateTime? StartedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public string? SupportingOfficer { get; set; }
        public Guid? SupportingOfficerId { get; set; }
        public Enums.WorkflowDecision? Decision { get; set; }
        public bool IsActive { get; set; }
        public bool CanAssignToMe { get; set; }
        public bool CanReject { get; set; }
    }
}
