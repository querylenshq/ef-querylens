using Share.Common.Workflow.Core.Domain;

namespace Share.Common.Workflow.Core.Application.Auth;

public class RoleTaskModel
{
    public Enums.MedicsRoleType RoleType { get; set; }

    public Enums.WorkflowType? WorkflowType { get; set; }
}
