using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Share.Common.Workflow.Core.Domain.Extensions;

public static class EnumExtensions
{
    public static Enums.MedicsRoleType ToMedicsRoleTypeEnum(this string roleName) =>
        roleName switch
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

    public static Enums.MedicsRoleType ToMedicsRoleType(this Enums.WorkflowRole workflowRole) =>
        workflowRole switch
        {
            Enums.WorkflowRole.AssignmentOfficer => Enums.MedicsRoleType.AssignmentOfficer,
            Enums.WorkflowRole.VerificationOfficer => Enums.MedicsRoleType.VerificationOfficer,
            Enums.WorkflowRole.EvaluationOfficer => Enums.MedicsRoleType.EvaluationOfficer,
            Enums.WorkflowRole.SupportingOfficer => Enums.MedicsRoleType.SupportingOfficer,
            Enums.WorkflowRole.ApprovingOfficer => Enums.MedicsRoleType.ApprovingOfficer,
            _ => 0
        };
}
