using Share.Common.Workflow.Core.Domain;
using Share.Lib.Abstractions.Api;

namespace Share.Common.Workflow.Api.Endpoints.Workflow;

public class GetWorkflowByTypeResponse : BaseEndpointResponse
{
    public WorkflowResponse Workflow { get; set; } = null!;
}

public class WorkflowResponse
{
    public Enums.WorkflowType WorkflowType { get; set; }
    public List<WorkflowLevelResponse> Levels { get; set; } = [];
}

public class WorkflowLevelResponse
{
    public int Level { get; set; }
    public bool IsFinal { get; set; }
    public Enums.WorkflowRole WorkflowRole { get; set; }
    public List<WorkflowLevelStageResponse> Stages { get; set; } = [];
}

public class WorkflowLevelStageResponse
{
    public int Stage { get; set; }
    public Enums.WorkflowStageIdentifier StageIdentifier { get; set; }
    public bool IsFinal { get; set; }
    public List<WorkflowLevelStagePrivilegeResponse> Privileges { get; set; } = [];
}

public class WorkflowLevelStagePrivilegeResponse
{
    public Enums.WorkflowPrivilegeType PrivilegeType { get; set; }
    public Enums.WorkflowPrivilegeRequirementType PrivilegeRequirementType { get; set; }
}
