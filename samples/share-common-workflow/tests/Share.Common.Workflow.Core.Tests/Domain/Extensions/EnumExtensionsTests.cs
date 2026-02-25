using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Domain.Extensions;
using Shouldly;

namespace Share.Common.Workflow.Core.Tests.Domain.Extensions;

public class EnumExtensionsTests
{
    [Theory]
    [InlineData("MOP", Enums.MedicsRoleType.Mop)]
    [InlineData("Mop", Enums.MedicsRoleType.Mop)]
    [InlineData("OTP", Enums.MedicsRoleType.OtpUser)]
    [InlineData("OtpUser", Enums.MedicsRoleType.OtpUser)]
    [InlineData("ASO", Enums.MedicsRoleType.AssignmentOfficer)]
    [InlineData("AssignmentOfficer", Enums.MedicsRoleType.AssignmentOfficer)]
    [InlineData("VO", Enums.MedicsRoleType.VerificationOfficer)]
    [InlineData("VerificationOfficer", Enums.MedicsRoleType.VerificationOfficer)]
    [InlineData("EO", Enums.MedicsRoleType.EvaluationOfficer)]
    [InlineData("EvaluationOfficer", Enums.MedicsRoleType.EvaluationOfficer)]
    [InlineData("SO", Enums.MedicsRoleType.SupportingOfficer)]
    [InlineData("SupportingOfficer", Enums.MedicsRoleType.SupportingOfficer)]
    [InlineData("AO", Enums.MedicsRoleType.ApprovingOfficer)]
    [InlineData("ApprovingOfficer", Enums.MedicsRoleType.ApprovingOfficer)]
    [InlineData("APP", Enums.MedicsRoleType.App)]
    [InlineData("App", Enums.MedicsRoleType.App)]
    [InlineData("Invalid", Enums.MedicsRoleType.Na)]
    public void ToMedicsRoleTypeEnum_ReturnsExpectedResult(
        string roleName,
        Enums.MedicsRoleType expectedResult
    )
    {
        // Act
        var result = roleName.ToMedicsRoleTypeEnum();

        // Assert
        result.ShouldBe(expectedResult);
    }
}
