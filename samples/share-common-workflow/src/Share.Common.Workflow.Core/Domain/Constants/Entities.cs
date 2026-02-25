namespace Share.Common.Workflow.Core.Domain;

public static partial class Constants
{
    public static class Entities
    {
        public static class Workflow
        {
            public const int NameMaxLength = 250;
        }

        public static class WorkflowLevel
        {
            public const int NameMaxLength = 250;
        }

        public static class ApplicationWorkflowLevelStageCondition
        {
            public const int RemarksMaxLength = 500;
        }

        public static class WorkflowLevelStage
        {
            public const int NameMaxLength = 200;
        }

        public static class AppWorkflowLevelStageAction
        {
            public const int RemarksMaxLength = 5000;
        }

        public static class MopProfile
        {
            public const int NameMaxLength = 250;
            public const int EmailMaxLength = 250;
        }

        public static class OfficerProfile
        {
            public const int NameMaxLength = 250;
            public const int EmailMaxLength = 250;
        }

        public static class Company
        {
            public const int NameMaxLength = 500;
            public const int UenNumberMaxLength = 50;
        }
    }
}
