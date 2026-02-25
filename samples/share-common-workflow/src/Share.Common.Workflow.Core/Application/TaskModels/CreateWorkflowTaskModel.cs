using Share.Common.Workflow.Core.Domain;

namespace Share.Common.Workflow.Core.Application.TaskModels;

public class CreateWorkflowTaskModel
{
    public Enums.WorkflowType WorkflowType { get; set; }

    public string Name { get; set; } = default!;

    public List<CreateWorkflowLevelTaskModel> Levels { get; set; } = [];

    public class CreateWorkflowLevelTaskModel
    {
        public int Level { get; set; }
        public string Name { get; set; } = default!;
        public bool IsFinal { get; set; }
        public Enums.WorkflowRole WorkflowRole { get; set; }

        public List<CreateWorkflowLevelStageTakModel> Stages { get; set; } = [];
    }

    public class CreateWorkflowLevelStageTakModel
    {
        public int Stage { get; set; }
        public string Name { get; set; } = default!;
        public Enums.WorkflowStageIdentifier StageIdentifier { get; set; }
        public bool IsFinal { get; set; }
        public List<CreateWorkflowLevelStagePrivilegeTaskModel> Privileges { get; set; } = [];
    }

    public class CreateWorkflowLevelStagePrivilegeTaskModel
    {
        public Enums.WorkflowPrivilegeType PrivilegeType { get; set; }
        public Enums.WorkflowPrivilegeRequirementType PrivilegeRequirementType { get; set; }
    }
}
