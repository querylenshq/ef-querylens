namespace Share.Common.Workflow.Core.Domain;

public static partial class Enums
{
    // There are values different compared to SHARE Medics Applications
    public enum WorkflowType
    {
        // MEDICS
        DealerLicenseNew = 10,
        DealerLicenseRenewal = 11,
        DealerLicenseAmendment = 12,
        DealerLicenseCancellation = 13,
        DealerLicenseFullfilmentOfApprovalCondition = 14,
        DealerLicenseApprovalConditionChange = 15,
        DealerLicenseStatusChange = 16,
        DealerLicenseFoacExtension = 17,
        ProductRegistrationNewImmediate = 20,
        ProductRegistrationRenewal = 21,
        ProductRegistrationAmendment = 22,
        ProductRegistrationNewExpedited = 24,
        ProductRegistrationFullfilmentOfApprovalCondition = 25,
        ProductRegistrationApprovalConditionChange = 26,
        ProductRegistrationStatusChange = 27,
        ProductRegistrationFoacExtension = 28,
        ChangeOfRegistrantNew = 30,
        ChangeNotificationNewAdministrativeOrNotificationSmdr = 40,
        ChangeNotificationNewTechnicalOrReview = 41,
        SpecialAccessRouteNew = 50,
        SpecialAccessRouteFullfilmentOfApprovalCondition = 51,
        SpecialAccessRouteApprovalConditionChange = 52,
        SpecialAccessRouteDistributionRecord = 53,
        SpecialAccessRouteFoacExtension = 54,
        ExportCertificateNew = 60,
        FreeSaleCertificateNew = 70,
        ProductNotificationStatusChange = 80,
        ImporterTaggingNew = 90,
        AppointmentBookingNew = 95,

        // DASH
        ProductNotificationNew = 100,

        ProductNotificationAmendment = 110,

        DealerNotificationNew = 200,

        DealerNotificationAmendment = 210,

        ProductRegistrationNew = 300,

        ProductRegistrationMajorVariation = 310,

        ProductRegistrationMinorVariation = 320,

        ProductRegistrationChangeOfRegistrant = 330,

        ProductRegistrationDeva = 340,

        ProductRegistrationRmp = 350,

        ProductRegistrationFulfilment = 360,

        ProductRegistrationRetention = 370,

        DealerLicenseNewImporterWholesalerLicense = 410,

        DealerLicenceUpdateImporterWholesalerLicence = 415,

        DealerLicenseNewManufacturerLicense = 420,

        DealerLicenceUpdateManufacturerLicence = 425,

        DealerLicenseCancelImporterWholesalerLicense = 430,

        DealerLicenseCancelManufacturerLicense = 440,

        DealerCertificateNewGDP = 510,

        DealerCertificateNewGDPWithoutTechnicalAssessment = 520,

        DealerCertificateNewGMP = 610,

        DealerCertificateNewGMPWithoutTechnicalAssessment = 620,

        AuditCaseILWL = 710,

        AuditCaseML = 720,

        PharmaceuticalProductNew = 800,

        FreeSaleNew = 805,
    }

    public enum WorkflowPrivilegeType
    {
        RaiseIr = 1,
        CreateChildCase = 2,
        ReviewDealerActivities = 3,
        ReviewProducts = 4,
        Approve = 5,
        Reject = 6,
        InitiateDeferredPayment = 7,
        RaiseQuery = 8,
        ExtendIr = 9,
        InstantApprove = 10,
        InstantReject = 11,
        GrantThirdPartyIrAccess = 12
    }

    public enum WorkflowPrivilegeRequirementType
    {
        RequiredToCompleteStage = 1,
        RequiredIfInitiated = 2,
        Optional = 3,
    }

    public enum WorkflowRole
    {
        AssignmentOfficer = 11,
        VerificationOfficer = 12,
        EvaluationOfficer = 13,
        SupportingOfficer = 14,
        ApprovingOfficer = 15
    }

    public enum WorkflowDecision
    {
        Pending = 1,
        Reject = 2,
        Approve = 3,
        ReturnForAmendment = 4,
        UnTag = 5,
        Assign = 6,
        InstantApprove = 7,
        InstantReject = 8,
    }

    public enum WorkflowStatus
    {
        InProgress = 1,
        Completed = 2,
        Terminated = 3,
    }
}
