using FastEndpoints;
using Share.Common.Workflow.Core.Application.Services;
using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Resources;

namespace Share.Common.Workflow.Api.Endpoints.Workflow;

public class GetWorkflowByType(WorkflowService service)
    : Endpoint<GetWorkflowByTypeRequest, GetWorkflowByTypeResponse>
{
    public override void Configure()
    {
        Get("/{workflowType:alpha}");
        Permissions(Constants.Permissions.Api.GetWorkflowByType);
    }

    public override async Task HandleAsync(GetWorkflowByTypeRequest req, CancellationToken ct)
    {
        var data = await service.GetWorkflowByTypeAsync(
            req.WorkflowType,
            w => new WorkflowResponse
            {
                WorkflowType = w.WorkflowType,
                Levels = w
                    .Levels.Where(l => l.IsNotDeleted)
                    .Select(l => new WorkflowLevelResponse
                    {
                        Level = l.Level,
                        IsFinal = l.IsFinal,
                        WorkflowRole = l.WorkflowRole,
                        Stages = l
                            .Stages.Where(s => s.IsNotDeleted)
                            .Select(s => new WorkflowLevelStageResponse
                            {
                                Stage = s.Stage,
                                StageIdentifier = s.StageIdentifier,
                                IsFinal = s.IsFinal,
                                Privileges = s
                                    .Privileges.Where(sp => sp.IsNotDeleted)
                                    .Select(sp => new WorkflowLevelStagePrivilegeResponse
                                    {
                                        PrivilegeType = sp.PrivilegeType,
                                        PrivilegeRequirementType = sp.PrivilegeRequirementType,
                                    })
                                    .ToList(),
                            })
                            .ToList()
                    })
                    .ToList(),
            },
            ct
        );

        if (data == null)
        {
            AddError(r => r.WorkflowType, WorkflowValidation.WorkflowNotFoundForType);

            ThrowIfAnyErrors();
        }

        await SendOkAsync(new GetWorkflowByTypeResponse { IsSuccess = true, Workflow = data! }, ct);
    }
}
