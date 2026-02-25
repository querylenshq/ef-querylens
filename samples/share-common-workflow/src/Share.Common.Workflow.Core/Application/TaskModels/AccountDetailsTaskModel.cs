using Share.Common.Workflow.Core.Domain;
using static Share.Common.Workflow.Core.Domain.Enums;

namespace Share.Common.Workflow.Core.Application.TaskModels;

public class AccountDetailsTaskModel
{
    public Guid AccountDetailsMessageTransactionId { get; set; }
    public Guid AccountId { get; set; }
    public required AccountType AccountType { get; set; }
    public required MessageRequestType MessageRequestType { get; set; }
    public required CamsUserActionType CamsUserActionType { get; set; }
    public required AccountStatus AccountStatus { get; set; }
    public string Name { get; set; } = default!;
    public AccountDetailsTaskModelInternetUserProfile? InternetUserProfile { get; set; }
    public AccountDetailsTaskModelIntranetUserProfile? IntranetUserProfile { get; set; }
    public List<AccountDetailsTaskModelAccountRole> Roles { get; set; } = [];
    public DateTime Timestamp { get; set; }
}

public class AccountDetailsTaskModelAccountRole
{
    public Guid RoleId { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }

    public required string RoleCode { get; set; }
    public required string Description { get; set; }
    public Enums.WorkflowType? WorkflowApplicationType { get; set; }
}

public class AccountDetailsTaskModelInternetUserProfile
{
    public Guid InternetUserProfileId { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }
    public required string IdentificationNoHash { get; set; }
    public string Designation { get; set; } = string.Empty;
    public List<AccountDetailsTaskModelInternetUserProfileCompany> Companies { get; set; } = [];
    public string? EmailAddress { get; set; }
}

public class AccountDetailsTaskModelInternetUserProfileCompany
{
    public Guid CompanyId { get; set; } = Guid.NewGuid();

    public required string Name { get; set; } = null!;

    public required string Uen { get; set; }

    public List<AccountDetailsTaskModelInternetUserProfileCompanyCompanyAddress> CompanyAddresses { get; set; } =
        [];

    public bool SingaporeRegistered { get; set; }
}

public class AccountDetailsTaskModelInternetUserProfileCompanyCompanyAddress
{
    public Guid CompanyAddressId { get; set; } = Guid.NewGuid();

    public required string PostalCode { get; set; }

    public required string BlockHouseNo { get; set; }

    public string? FloorNo { get; set; }

    public string? UnitNo { get; set; }

    public required string StreetName { get; set; }

    public string? BuildingName { get; set; }

    public string? Other { get; set; }
}

public class AccountDetailsTaskModelIntranetUserProfile
{
    public Guid IntranetUserProfileId { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }
    public required string SoeId { get; set; } = null!;

    public required string Designation { get; set; } = null!;

    public string? UserEmail { get; set; } = null!;
}

public class MopProfileCompanyTaskModel
{
    public Guid MopProfileId { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; } = Guid.NewGuid();

    public required string Name { get; set; } = null!;

    public required string Uen { get; set; }

    public List<AccountDetailsTaskModelInternetUserProfileCompanyCompanyAddress> CompanyAddresses { get; set; } =
        [];

    public bool SingaporeRegistered { get; set; }
}
