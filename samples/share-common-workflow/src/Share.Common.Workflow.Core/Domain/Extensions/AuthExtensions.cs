using Share.Common.Workflow.Core.Application.Auth;

namespace Share.Common.Workflow.Core.Domain.Extensions;

public static class AuthExtensions
{
    public static Enums.MedicsRoleType GetRoleFromClaimString(this string roleString)
    {
        Enums.MedicsRoleType role = roleString switch
        {
            "MOP" => Enums.MedicsRoleType.Mop,
            "OTP" => Enums.MedicsRoleType.OtpUser,
            "ASO" => Enums.MedicsRoleType.AssignmentOfficer,
            "VO" => Enums.MedicsRoleType.VerificationOfficer,
            "EO" => Enums.MedicsRoleType.EvaluationOfficer,
            "SO" => Enums.MedicsRoleType.SupportingOfficer,
            "AO" => Enums.MedicsRoleType.ApprovingOfficer,
            "ReadOnlyOfficer" => Enums.MedicsRoleType.ReadOnlyOfficer,
            "PO" => Enums.MedicsRoleType.ProductOwner,
            "APP" or "App" => Enums.MedicsRoleType.App,
            _ => 0
        };

        if (role == 0)
        {
            return !Enum.TryParse(roleString, out role) ? 0 : role;
        }

        return role;
    }

    public static List<RoleClaimModel> GetRoleClaims(this List<string> roles)
    {
        var rolesList = new List<RoleClaimModel>();
        foreach (string role in roles)
        {
            if (role == "APP" || role == "App")
            {
                rolesList.Add(new RoleClaimModel() { AppClientCode = "MEDICS", RoleCode = role });
            }
            else
            {
                var currentRole = role.Split('|').ToList();
                if (currentRole.Count > 1)
                {
                    var claimModel = new RoleClaimModel()
                    {
                        AppClientCode = currentRole[0],
                        RoleCode = currentRole[1],
                    };

                    if (currentRole.Count == 3)
                    {
                        claimModel.WorkflowCode = currentRole[2];
                    }
                    else if (currentRole.Count == 4)
                    {
                        claimModel.WorkflowCode = currentRole[3];
                    }

                    if (
                        claimModel.RoleType != Enums.MedicsRoleType.Na
                        && (
                            claimModel.AppClientCode.Equals("SHARE")
                            || claimModel.AppClientCode.Equals("MEDICS")
                        )
                    )
                    {
                        rolesList.Add(claimModel);
                    }
                }
            }
        }

        rolesList = rolesList
            .DistinctBy(x => new
            {
                x.AppClientCode,
                x.RoleType,
                x.WorkflowType
            })
            .ToList();
        return rolesList;
    }
}
