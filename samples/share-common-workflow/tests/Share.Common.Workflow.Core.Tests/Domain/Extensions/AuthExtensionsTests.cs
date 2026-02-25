using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Domain.Extensions;
using Shouldly;

namespace Share.Common.Workflow.Core.Tests.Domain.Extensions;

public class AuthExtensionsTests
{
    [Theory]
    [InlineData("MOP", Enums.MedicsRoleType.Mop)]
    [InlineData("OTP", Enums.MedicsRoleType.OtpUser)]
    [InlineData("ASO", Enums.MedicsRoleType.AssignmentOfficer)]
    [InlineData("VO", Enums.MedicsRoleType.VerificationOfficer)]
    [InlineData("EO", Enums.MedicsRoleType.EvaluationOfficer)]
    [InlineData("SO", Enums.MedicsRoleType.SupportingOfficer)]
    [InlineData("AO", Enums.MedicsRoleType.ApprovingOfficer)]
    [InlineData("Invalid", Enums.MedicsRoleType.Na)]
    public void GetRoleFromClaimString_ReturnsExpectedResult(
        string claimString,
        Enums.MedicsRoleType expectedResult
    )
    {
        // Act
        var result = claimString.GetRoleFromClaimString();

        // Assert
        result.ShouldBe(expectedResult);
    }
}
