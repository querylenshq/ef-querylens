namespace Share.Common.Workflow.Core.Domain;

public static partial class Constants
{
    public static class WorkflowConstants
    {
        public const string MedicsAppClientCode = "MEDICS";
        public const string ShareAppClientCode = "SHARE";
        public static readonly IReadOnlyDictionary<
            Enums.WorkflowDecision,
            Enums.WorkflowPrivilegeType
        > DecisionToPrivilegeTypeMap = new Dictionary<
            Enums.WorkflowDecision,
            Enums.WorkflowPrivilegeType
        >()
        {
            { Enums.WorkflowDecision.Approve, Enums.WorkflowPrivilegeType.Approve },
            { Enums.WorkflowDecision.Reject, Enums.WorkflowPrivilegeType.Reject },
            { Enums.WorkflowDecision.InstantApprove, Enums.WorkflowPrivilegeType.InstantApprove },
            { Enums.WorkflowDecision.InstantReject, Enums.WorkflowPrivilegeType.InstantReject }
        };

        public static readonly IReadOnlyCollection<Enums.WorkflowType> CnWorkflowTypes =
        [
            Enums.WorkflowType.ChangeNotificationNewAdministrativeOrNotificationSmdr,
            Enums.WorkflowType.ChangeNotificationNewTechnicalOrReview
        ];
    }
}
