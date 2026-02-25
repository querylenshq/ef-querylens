namespace Share.Common.Workflow.Core.Domain;

public static partial class Enums
{
    public enum MedicsRoleType
    {
        Na = 0,
        Mop = 1,
        OtpUser = 2,
        AssignmentOfficer = 11,
        VerificationOfficer = 12,
        EvaluationOfficer = 13,
        SupportingOfficer = 14,
        ApprovingOfficer = 15,
        ReadOnlyOfficer = 16,
        ProductOwner = 17,
        App = 20
    }

    public enum AccountStatus
    {
        Active = 1,
        Inactive = 2
    }
}
