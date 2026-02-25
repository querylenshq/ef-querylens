using Share.Common.Workflow.Core.Domain;

namespace Share.Common.Workflow.Core.Application.Auth;

public class RoleClaimModel
{
    public string AppClientCode { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;

    public Enums.MedicsRoleType RoleType
    {
        get
        {
            return RoleCode switch
            {
                "MOP" or "Mop" => Enums.MedicsRoleType.Mop,
                "OTP" or "OtpUser" => Enums.MedicsRoleType.OtpUser,
                "ASO" or "AssignmentOfficer" => Enums.MedicsRoleType.AssignmentOfficer,
                "VO" or "VerificationOfficer" => Enums.MedicsRoleType.VerificationOfficer,
                "EO" or "EvaluationOfficer" => Enums.MedicsRoleType.EvaluationOfficer,
                "SO" or "SupportingOfficer" => Enums.MedicsRoleType.SupportingOfficer,
                "AO" or "ApprovingOfficer" => Enums.MedicsRoleType.ApprovingOfficer,
                "ReadOnlyOfficer" => Enums.MedicsRoleType.ReadOnlyOfficer,
                "PO" => Enums.MedicsRoleType.ProductOwner,
                "APP" or "App" => Enums.MedicsRoleType.App,
                _ => 0
            };
        }
    }

    public Enums.WorkflowRole WorkflowRoleType
    {
        get
        {
            Enums.WorkflowRole role = RoleCode switch
            {
                "ASO" or "AssignmentOfficer" => Enums.WorkflowRole.AssignmentOfficer,
                "VO" or "VerificationOfficer" => Enums.WorkflowRole.VerificationOfficer,
                "EO" or "EvaluationOfficer" => Enums.WorkflowRole.EvaluationOfficer,
                "SO" or "SupportingOfficer" => Enums.WorkflowRole.SupportingOfficer,
                "AO" or "ApprovingOfficer" => Enums.WorkflowRole.ApprovingOfficer,
                _ => 0
            };

            if (role == 0)
            {
                return !Enum.TryParse(RoleCode, out role) ? 0 : role;
            }

            return role;
        }
    }

    public string? WorkflowCode { get; set; }

    public Enums.WorkflowType? WorkflowType
    {
        get { return Enum.TryParse<Enums.WorkflowType>(WorkflowCode, out var type) ? type : null; }
    }
}
