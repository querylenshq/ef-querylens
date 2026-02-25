namespace Share.Common.Workflow.Api.Endpoints.Applications;

public class AssignWorkflowStageRequest
{
    public Guid ApplicationId { get; set; }
    public int Stage { get; set; }
    public int Level { get; set; }

    public Guid? AssingeeAccountId { get; set; }
    public Guid? AssigneerAccountId { get; set; }
    public string? Remarks { get; set; }
}
