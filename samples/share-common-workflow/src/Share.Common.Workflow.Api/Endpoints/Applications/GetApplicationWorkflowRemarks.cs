using FastEndpoints;
using Share.Common.Workflow.Core.Application.Services;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Abstractions.Api;

namespace Share.Common.Workflow.Api.Endpoints.Applications;

public class GetApplicationWorkflowRemarks(ApplicationWorkflowService workflowService)
    : Endpoint<GetApplicationWorkflowRemarksRequest, GetApplicationWorkflowRemarksResponse>
{
    public override void Configure()
    {
        Get("/applications/{applicationId:guid}/remarks");
        Permissions(Constants.Permissions.Api.Applications.GetApplicationWorkflowRemarks);
    }

    public override async Task HandleAsync(
        GetApplicationWorkflowRemarksRequest req,
        CancellationToken ct
    )
    {
        var workflow = await workflowService.GetAppWorkflowByIdAsync(
            req.ApplicationId,
            workflow => workflow,
            ct
        );

        if (workflow == null)
        {
            await SendOkAsync(new GetApplicationWorkflowRemarksResponse { IsSuccess = false }, ct);
            return;
        }

        var actions = await workflowService.GetAppWorkflowRemarksByApplicationIdAsync(
            req.ApplicationId,
            s => new GetApplicationWorkflowRemarksResponse.ApplicationWorkflowStageItem
            {
                StartedAt = s.StartedAt,
                CompletedAt = s.CompletedAt,
                Remarks = s.Remarks,
                SupportingOfficer = s.AppOfficer.Officer.Name
            },
            ct
        );

        var res = new GetApplicationWorkflowRemarksResponse { IsSuccess = true, Stages = actions };

        await SendOkAsync(res, ct);
    }
}

public class GetApplicationWorkflowRemarksRequest
{
    public Guid ApplicationId { get; set; }
}

public class GetApplicationWorkflowRemarksResponse : BaseEndpointResponse
{
    public List<ApplicationWorkflowStageItem> Stages { get; set; } = [];

    public class ApplicationWorkflowStageItem
    {
        public DateTime? StartedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public string? Remarks { get; set; }

        public string? SupportingOfficer { get; set; }
    }
}
