namespace Share.Common.Workflow.Core.Domain;

public static partial class Constants
{
    public static class Permissions
    {
        public static class Api
        {
            public static class Applications
            {
                public const string CreateApplication = "api.wf.application.create";
                public const string GetApplicationWorkflow = "api.wf.application.get";
                public const string GetApplicationWorkflowRemarks =
                    "api.wf.application.remarks.get";
                public const string AssignWorkflowStage = "api.wf.application.assignWorkflowStage";
                public const string PostWorkflowStageDecision =
                    "api.wf.application.postWorkflowStageDecision";
                public const string CompleteApplicationWorkflow =
                    "api.wf.application.completeApplicationWorkflow";
            }

            public const string GetWorkflowByType = "api.wf.workflow.get";
        }
    }
}
