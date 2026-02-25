using Share.Common.Workflow.Core.Domain;

namespace Share.Common.Workflow.Api.Endpoints.Workflow;

public class GetWorkflowByTypeRequest
{
    public Enums.WorkflowType WorkflowType { get; set; }
}
