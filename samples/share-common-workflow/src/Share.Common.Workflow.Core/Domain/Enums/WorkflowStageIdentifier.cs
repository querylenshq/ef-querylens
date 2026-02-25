namespace Share.Common.Workflow.Core.Domain;

public static partial class Enums
{
    public enum WorkflowStageIdentifier
    {
        None = 0,
        Verification = 100,
        AcceptForEvaluation = 200,
        Evaluation = 300,
        Support = 400,
        Approval = 500,
    }
}
