using System.Linq.Expressions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share.Common.Workflow.Core.Application.Auth;
using Share.Common.Workflow.Core.Application.TaskModels;
using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Domain.Extensions;
using Share.Common.Workflow.Core.Entities;
using Share.Common.Workflow.Core.Extensions;
using Share.Common.Workflow.Core.Infrastructure.Interfaces;
using Share.Lib.Abstractions.Api;
using Share.Lib.Abstractions.Common.Interfaces;
using Share.Lib.Bootstrap.Api.Core.Application.Interfaces;
using Share.Lib.Bootstrap.Api.Core.Entities;

namespace Share.Common.Workflow.Core.Application.Services;

public class WorkflowAccountService(
    IWorkflowDbContext context,
    ILogger<WorkflowAccountService> logger,
    ICurrentUser currentUser
) : IAccountService
{
    public async Task<List<(Enums.MedicsRoleType, Enums.WorkflowType?)>> GetOfficerRolesAsync(
        Guid accountId,
        CancellationToken ct
    )
    {
        var roles = await context
            .MedicsAccountRoles.AsNoTracking()
            .Where(s => s.IsNotDeleted && s.AccountId == accountId)
            .Select(s => new { s.MedicsRole.RoleType, s.MedicsRole.WorkflowType })
            .Distinct()
            .ToListAsync(ct);

        return roles.Select(r => (r.RoleType, r.WorkflowType)).ToList();
    }

    public async Task SetAccountInformationAsync(
        List<AccountDetailsTaskModel> taskModels,
        CancellationToken ct
    )
    {
        var accountIds = taskModels.Select(taskModel => taskModel.AccountId).ToList();
        var accounts = await context
            .Accounts.Where(account => accountIds.Contains(account.AccountId))
            .ToListAsync(ct);

        var newAccountsToAdd = new List<Account>();

        foreach (var taskModel in taskModels)
        {
            var account = accounts.SingleOrDefault(account =>
                account.AccountId == taskModel.AccountId
            );
            if (account == null)
            {
                newAccountsToAdd.Add(
                    new Account
                    {
                        AccountId = taskModel.AccountId,
                        Name = taskModel.Name,
                        CreatedById = currentUser.UserAccountId
                    }
                );
            }
            else
            {
                account.AccountId = taskModel.AccountId;
                account.Name = taskModel.Name;
                account.LastModifiedById = currentUser.UserAccountId;
            }
        }

        await context.Accounts.AddRangeAsync(newAccountsToAdd, ct);

        var excludedAccountIdToUpdate = await GetExcludedAccountToUpdateAsync(taskModels, ct);
        taskModels = taskModels
            .Where(w => !excludedAccountIdToUpdate.Contains(w.AccountId))
            .ToList();

        await SetInternetUserProfilesAsync(taskModels, ct);
        await SetIntranetUserProfilesAsync(taskModels, ct);
        await SetAccountRolesAsync(taskModels, ct);

        await context.SaveChangesAsync(ct);
    }

    private async Task<List<Guid>> GetExcludedAccountToUpdateAsync(
        List<AccountDetailsTaskModel> taskModels,
        CancellationToken ct
    )
    {
        var accountIds = taskModels.Select(taskModel => taskModel.AccountId).ToList();
        var excludedAccounts = new List<Guid>();

        var intranetUserProfiles = await context
            .OfficerProfiles.AsNoTracking()
            .Where(officerProfile => accountIds.Contains(officerProfile.AccountId))
            .ToListAsync(ct);

        var internetUserProfiles = await context
            .MopProfiles.AsNoTracking()
            .Where(mopProfile => accountIds.Contains(mopProfile.AccountId))
            .ToListAsync(ct);

        foreach (var intranetUserProfile in intranetUserProfiles)
        {
            if (
                intranetUserProfile.LastSyncTimestamp
                > taskModels.Single(s => s.AccountId == intranetUserProfile.AccountId).Timestamp
            )
            {
                excludedAccounts.Add(intranetUserProfile.AccountId);
            }
        }

        foreach (var internetUserProfile in internetUserProfiles)
        {
            if (
                internetUserProfile.LastSyncTimestamp
                > taskModels.Single(s => s.AccountId == internetUserProfile.AccountId).Timestamp
            )
            {
                excludedAccounts.Add(internetUserProfile.AccountId);
            }
        }

        return excludedAccounts;
    }

    private async Task SetIntranetUserProfilesAsync(
        List<AccountDetailsTaskModel> taskModels,
        CancellationToken ct
    )
    {
        taskModels = taskModels.Where(taskModel => taskModel.IntranetUserProfile != null).ToList();

        var accountIds = taskModels.Select(taskModel => taskModel.AccountId).ToList();

        var intranetUserProfiles = await context
            .OfficerProfiles.Where(officerProfile => accountIds.Contains(officerProfile.AccountId))
            .ToListAsync(ct);

        var newIntranetUserProfilesToAdd = new List<OfficerProfile>();

        foreach (var taskModel in taskModels)
        {
            var intranetUserProfile = intranetUserProfiles.SingleOrDefault(officerProfile =>
                officerProfile.AccountId == taskModel.AccountId
            );

            if (intranetUserProfile == null)
            {
                intranetUserProfile = new OfficerProfile
                {
                    OfficerProfileId = taskModel.IntranetUserProfile!.IntranetUserProfileId,
                    AccountId = taskModel.AccountId,
                    Name = taskModel.Name,
                    Email =
                        taskModel.IntranetUserProfile!.UserEmail != null
                            ? taskModel.IntranetUserProfile!.UserEmail
                            : string.Empty,
                    Designation = taskModel.IntranetUserProfile!.Designation,
                    SoeId = taskModel.IntranetUserProfile!.SoeId,
                    Status = Domain.Enums.AccountStatus.Active, // TODO sync this also
                    CreatedById = currentUser.UserAccountId,
                    LastSyncTimestamp = taskModel.Timestamp,
                    LastTransactionId = taskModel.AccountDetailsMessageTransactionId
                };

                newIntranetUserProfilesToAdd.Add(intranetUserProfile);
            }
            else
            {
                intranetUserProfile.AccountId = taskModel.AccountId;
                intranetUserProfile.Name = taskModel.Name;
                intranetUserProfile.Email =
                    taskModel.IntranetUserProfile!.UserEmail != null
                        ? taskModel.IntranetUserProfile!.UserEmail
                        : string.Empty;
                intranetUserProfile.Designation = taskModel.IntranetUserProfile!.Designation;
                intranetUserProfile.SoeId = taskModel.IntranetUserProfile!.SoeId;
                intranetUserProfile.LastModifiedById = currentUser.UserAccountId;
                intranetUserProfile.LastSyncTimestamp = taskModel.Timestamp;
                intranetUserProfile.LastTransactionId =
                    taskModel.AccountDetailsMessageTransactionId;
            }
        }
        await context.OfficerProfiles.AddRangeAsync(newIntranetUserProfilesToAdd, ct);
    }

    private async Task SetInternetUserProfilesAsync(
        List<AccountDetailsTaskModel> taskModels,
        CancellationToken ct
    )
    {
        taskModels = taskModels.Where(taskModel => taskModel.InternetUserProfile != null).ToList();

        var accountIds = taskModels.Select(taskModel => taskModel.AccountId).ToList();

        var internetUserProfiles = await context
            .MopProfiles.Where(mopProfile => accountIds.Contains(mopProfile.AccountId))
            .ToListAsync(ct);

        var newInternetUserProfilesToAdd = new List<MopProfile>();

        var mopProfileCompanyTaskModels = new List<MopProfileCompanyTaskModel>();

        foreach (var taskModel in taskModels)
        {
            if (taskModel.InternetUserProfile == null)
            {
                continue;
            }

            var internetUserProfile = internetUserProfiles.SingleOrDefault(mopProfile =>
                mopProfile.AccountId == taskModel.AccountId
            );

            if (internetUserProfile == null)
            {
                internetUserProfile = new MopProfile
                {
                    MopProfileId = taskModel.InternetUserProfile!.InternetUserProfileId,
                    AccountId = taskModel.AccountId,
                    Name = taskModel.Name,
                    Email =
                        taskModel.InternetUserProfile!.EmailAddress != null
                            ? taskModel.InternetUserProfile!.EmailAddress
                            : string.Empty,
                    CreatedById = currentUser.UserAccountId,
                    LastSyncTimestamp = taskModel.Timestamp,
                    LastTransactionId = taskModel.AccountDetailsMessageTransactionId
                };

                newInternetUserProfilesToAdd.Add(internetUserProfile);
            }
            else
            {
                internetUserProfile.AccountId = taskModel.AccountId;
                internetUserProfile.Name = taskModel.Name;
                internetUserProfile.Email =
                    taskModel.InternetUserProfile!.EmailAddress != null
                        ? taskModel.InternetUserProfile!.EmailAddress
                        : string.Empty;
                internetUserProfile.LastModifiedById = currentUser.UserAccountId;
                internetUserProfile.LastSyncTimestamp = taskModel.Timestamp;
                internetUserProfile.LastTransactionId =
                    taskModel.AccountDetailsMessageTransactionId;
            }

            mopProfileCompanyTaskModels.AddRange(
                taskModel.InternetUserProfile!.Companies.Select(
                    company => new MopProfileCompanyTaskModel
                    {
                        MopProfileId = internetUserProfile.MopProfileId,
                        CompanyId = company.CompanyId,
                        Name = company.Uen,
                        Uen = company.Uen,
                        CompanyAddresses = company.CompanyAddresses,
                        SingaporeRegistered = company.SingaporeRegistered,
                    }
                )
            );
        }

        await context.MopProfiles.AddRangeAsync(newInternetUserProfilesToAdd, ct);

        await SetMopProfileCompaniesAsync(mopProfileCompanyTaskModels, ct);
    }

    private async Task SetMopProfileCompaniesAsync(
        List<MopProfileCompanyTaskModel> taskModels,
        CancellationToken ct
    )
    {
        var mopProfileIds = taskModels.Select(taskModel => taskModel.MopProfileId).ToList();
        var mopProfileCompanies = await context
            .MopProfileCompanies.Where(company => mopProfileIds.Contains(company.MopProfileId))
            .ToListAsync(ct);

        var companies = await context
            .Companies.Where(company =>
                taskModels.Select(taskModel => taskModel.CompanyId).Contains(company.CompanyId)
            )
            .ToListAsync(ct);

        var newMopProfileCompaniesToAdd = new List<MopProfileCompany>();

        var newCompaniesToAdd = new List<Company>();

        foreach (var taskModel in taskModels)
        {
            var company = companies.SingleOrDefault(company =>
                company.CompanyId == taskModel.CompanyId
            );
            if (company == null)
            {
                company = newCompaniesToAdd.SingleOrDefault(company =>
                    company.CompanyId == taskModel.CompanyId
                );
            }
            if (company == null)
            {
                company = new Company
                {
                    CompanyId = taskModel.CompanyId,
                    Name = taskModel.Name,
                    UenNumber = taskModel.Uen,
                    CreatedById = currentUser.UserAccountId,
                };
                newCompaniesToAdd.Add(company);
            }
            else
            {
                company.Name = taskModel.Name;
                company.UenNumber = taskModel.Uen;
                company.LastModifiedById = currentUser.UserAccountId;
            }

            var mopProfileCompany = mopProfileCompanies.SingleOrDefault(company =>
                company.MopProfileId == taskModel.MopProfileId
                && company.CompanyId == taskModel.CompanyId
            );
            if (mopProfileCompany == null)
            {
                mopProfileCompany = newMopProfileCompaniesToAdd.SingleOrDefault(company =>
                    company.MopProfileId == taskModel.MopProfileId
                    && company.CompanyId == taskModel.CompanyId
                );
            }
            if (mopProfileCompany == null)
            {
                newMopProfileCompaniesToAdd.Add(
                    new MopProfileCompany
                    {
                        MopProfileId = taskModel.MopProfileId,
                        CompanyId = company.CompanyId,
                        CreatedById = currentUser.UserAccountId,
                    }
                );
            }
            else
            {
                mopProfileCompany.MopProfileId = taskModel.MopProfileId;
                mopProfileCompany.CompanyId = taskModel.CompanyId;
                mopProfileCompany.LastModifiedById = currentUser.UserAccountId;
            }
        }

        await context.Companies.AddRangeAsync(newCompaniesToAdd, ct);
        await context.MopProfileCompanies.AddRangeAsync(newMopProfileCompaniesToAdd);
    }

    private async Task SetAccountRolesAsync(
        List<AccountDetailsTaskModel> taskModels,
        CancellationToken ct
    )
    {
        var accountIds = taskModels.Select(taskModel => taskModel.AccountId).ToList();
        var medicsRoleTypes = taskModels
            .SelectMany(taskModel => taskModel.Roles)
            .Select(role => new RoleTaskModel
            {
                RoleType = role.RoleCode.ToMedicsRoleTypeEnum(),
                WorkflowType = role.WorkflowApplicationType
            })
            .DistinctBy(d => new { d.RoleType, d.WorkflowType })
            .ToList();

        var accountRoles = await context
            .MedicsAccountRoles.Include(accountRole => accountRole.MedicsRole)
            .Where(accountRole =>
                accountRole.IsNotDeleted
                && accountRole.MedicsRole.IsNotDeleted
                && accountIds.Contains(accountRole.AccountId)
            )
            .ToListAsync(ct);

        var allRoles = await context.MedicsRoles.Where(m => m.IsNotDeleted).ToListAsync(ct);
        var medicsRoles = allRoles
            .Where(m =>
                medicsRoleTypes.Any(rtm =>
                    rtm.RoleType == m.RoleType && rtm.WorkflowType == m.WorkflowType
                )
            )
            .ToList();

        var newAccountRolesToAdd = new List<MedicsAccountRole>();
        var newMedicsRolesToAdd = new List<MedicsRole>();
        var newListAccountRoleIds = new List<(Guid AccountId, Guid MedicsRoleId)>();

        var distinctRoles = taskModels
            .SelectMany(taskModel =>
                taskModel.Roles.Select(role =>
                    (taskModel.AccountId, role.RoleCode, role.WorkflowApplicationType)
                )
            )
            .Distinct()
            .ToList();

        foreach (var (accountId, role, wfType) in distinctRoles)
        {
            var medicsRole = medicsRoles.SingleOrDefault(medicsRole =>
                medicsRole.RoleType == role.ToMedicsRoleTypeEnum()
                && wfType == medicsRole.WorkflowType
            );
            if (medicsRole == null)
            {
                medicsRole = newMedicsRolesToAdd.SingleOrDefault(medicsRole =>
                    medicsRole.RoleType == role.ToMedicsRoleTypeEnum()
                    && wfType == medicsRole.WorkflowType
                );
            }
            if (medicsRole == null)
            {
                medicsRole = new MedicsRole
                {
                    RoleType = role.ToMedicsRoleTypeEnum(),
                    WorkflowType = wfType,
                };
                newMedicsRolesToAdd.Add(medicsRole);
            }

            var accountRole = accountRoles.SingleOrDefault(accountRole =>
                accountRole.AccountId == accountId
                && accountRole.MedicsRoleId == medicsRole.MedicsRoleId
            );

            if (accountRole == null)
            {
                accountRole = newAccountRolesToAdd.SingleOrDefault(accountRole =>
                    accountRole.AccountId == accountId
                    && accountRole.MedicsRoleId == medicsRole.MedicsRoleId
                );
            }

            if (accountRole == null)
            {
                newAccountRolesToAdd.Add(
                    new MedicsAccountRole
                    {
                        AccountId = accountId,
                        MedicsRoleId = medicsRole.MedicsRoleId,
                        CreatedById = currentUser.UserAccountId,
                    }
                );
            }
            else
            {
                accountRole.IsDeleted = false;
            }

            newListAccountRoleIds.Add((accountId, medicsRole.MedicsRoleId));
        }

        await context.MedicsRoles.AddRangeAsync(newMedicsRolesToAdd, ct);
        await context.MedicsAccountRoles.AddRangeAsync(newAccountRolesToAdd, ct);

        var toDeleteGroupByAccountId = newListAccountRoleIds.GroupBy(a => a.AccountId).ToList();
        foreach (var newListAccountRoleId in toDeleteGroupByAccountId)
        {
            var currentAccountRoleIds = newListAccountRoleId.Select(s => s.MedicsRoleId).ToList();
            var rolesToRemove = accountRoles
                .Where(w =>
                    w.AccountId == newListAccountRoleId.Key
                    && !currentAccountRoleIds.Contains(w.MedicsRoleId)
                )
                .ToList();

            rolesToRemove.SetIsDeleted();
        }
    }

    private async Task SetAccountRolesAsync(
        Guid accountId,
        List<RoleClaimModel> roles,
        CancellationToken ct
    )
    {
        var accountRoles = await context
            .MedicsAccountRoles.Include(r => r.MedicsRole)
            .Where(w => w.AccountId == accountId && w.IsNotDeleted)
            .ToListAsync(ct);

        var medicsRoles = await context.MedicsRoles.Where(w => w.IsNotDeleted).ToListAsync(ct);

        var distinctRoles = roles
            .Select(s => new { s.RoleType, s.WorkflowType })
            .Distinct()
            .ToList();

        var newListOfRoleId = new List<Guid>();
        foreach (var role in distinctRoles)
        {
            var medicsRole = medicsRoles.SingleOrDefault(s =>
                s.RoleType == role.RoleType && s.WorkflowType == role.WorkflowType
            );

            if (medicsRole == null)
            {
                medicsRole = new MedicsRole
                {
                    RoleType = role.RoleType,
                    WorkflowType = role.WorkflowType
                };

                await context.MedicsRoles.AddAsync(medicsRole, ct);
            }

            var accountRoleExists = accountRoles.Exists(a =>
                a.MedicsRoleId == medicsRole.MedicsRoleId
            );
            if (!accountRoleExists)
            {
                var accountRole = new MedicsAccountRole
                {
                    AccountId = accountId,
                    MedicsRoleId = medicsRole.MedicsRoleId,
                    CreatedById = accountId
                };

                await context.MedicsAccountRoles.AddAsync(accountRole, ct);
            }

            newListOfRoleId.Add(medicsRole.MedicsRoleId);
        }

        var rolesToRemove = accountRoles
            .Where(w => !newListOfRoleId.Contains(w.MedicsRoleId))
            .ToList();

        rolesToRemove.ForEach(r =>
        {
            r.IsDeleted = true;
            r.LastModifiedById = accountId;
        });
    }

    public async Task SetAccountInformationAsync(
        Guid accountId,
        string name,
        Dictionary<string, IEnumerable<string>> claims,
        CancellationToken ct
    )
    {
        var roles = GetRoles(claims);

        var isValidRoles = ValidateUserRoles(accountId, name, roles);

        if (!isValidRoles)
        {
            logger.LogInformation("User {Name} {AccountId} has invalid roles", name, accountId);
            return;
        }

        var account = await context.Accounts.SingleOrDefaultAsync(
            s => s.AccountId == accountId,
            ct
        );

        logger.LogInformation(
            "User {Name} {AccountId} account exists: {Exists}",
            name,
            accountId,
            account == null
        );

        if (account == null)
        {
            account = new Account
            {
                AccountId = accountId,
                Name = name,
                CreatedById = accountId
            };
            await context.Accounts.AddAsync(account, ct);
        }
        else
        {
            account.Name = name;
            account.LastModifiedById = accountId;
        }

        if (
            roles.Select(s => s.RoleType).ToList().Contains(Enums.MedicsRoleType.Mop)
            || roles.Select(s => s.RoleType).ToList().Contains(Enums.MedicsRoleType.OtpUser)
        )
        {
            var mopProfileId = await SetMopProfileAsync(accountId, name, ct);

            await SetMopCompanyAsync(accountId, mopProfileId, claims, ct);
        }
        else if (!roles.Select(s => s.RoleType).ToList().Contains(Enums.MedicsRoleType.App))
        {
            await SetOfficerProfileAsync(accountId, name, claims, ct);
        }

        await SetAccountRolesAsync(accountId, roles, ct);

        await context.SaveChangesAsync(ct);
    }

    public Task<TResult?> GetAccountByIdAsync<TResult>(
        Guid accountId,
        Expression<Func<Account, TResult>> expression,
        CancellationToken ct
    )
    {
        throw new NotImplementedException();
    }

    public Task<Dictionary<string, IEnumerable<string>>> GetAccountClaimsAsync(
        Guid accountId,
        CancellationToken ct
    )
    {
        throw new NotImplementedException();
    }

    private async Task SetOfficerProfileAsync(
        Guid accountId,
        string name,
        Dictionary<string, IEnumerable<string>> claims,
        CancellationToken ct
    )
    {
        var officerProfile = await context.OfficerProfiles.SingleOrDefaultAsync(
            s => s.AccountId == accountId,
            ct
        );

        var email = claims.GetValueOrDefault(Constants.MedicsAuth.Claims.Email)?.SingleOrDefault();
        if (officerProfile == null)
        {
            officerProfile = new OfficerProfile
            {
                AccountId = accountId,
                Name = name,
                Email = email ?? string.Empty,
                Status = Enums.AccountStatus.Active,
                CreatedById = accountId
            };

            await context.OfficerProfiles.AddAsync(officerProfile, ct);

            return;
        }

        officerProfile.Name = name;
        officerProfile.Email = email ?? officerProfile.Email;
        officerProfile.LastModifiedById = accountId;
    }

    private async Task SetMopCompanyAsync(
        Guid accountId,
        Guid mopProfileId,
        Dictionary<string, IEnumerable<string>> claims,
        CancellationToken ct
    )
    {
        var companyIdClaim = claims
            .GetValueOrDefault(Constants.MedicsAuth.Claims.CompanyId)
            ?.FirstOrDefault();

        if (
            string.IsNullOrEmpty(companyIdClaim)
            || !Guid.TryParse(companyIdClaim, out var companyId)
        )
        {
            logger.LogWarning("Invalid company id {CompanyId}", companyIdClaim);
            return;
        }

        var company = await context.Companies.SingleOrDefaultAsync(
            s => s.CompanyId == companyId,
            ct
        );

        var companyName = claims
            .GetValueOrDefault(Constants.MedicsAuth.Claims.CompanyName)
            ?.SingleOrDefault();
        var companyUen = claims
            .GetValueOrDefault(Constants.MedicsAuth.Claims.CompanyUen)
            ?.SingleOrDefault();

        logger.LogInformation(
            "companyUen {CompanyUen} : companyName {CompanyName}",
            companyName,
            companyUen
        );

        if (company == null)
        {
            company = new Company
            {
                CompanyId = companyId,
                Name = companyName ?? string.Empty,
                UenNumber = companyUen ?? string.Empty,
                CreatedById = accountId
            };

            await context.Companies.AddAsync(company, ct);
        }
        else
        {
            company.Name = companyName ?? company.Name;
            company.UenNumber = companyUen ?? company.UenNumber;
            company.LastModifiedById = accountId;
        }

        var mopCompanyExists = await context.MopProfileCompanies.AnyAsync(
            s => s.CompanyId == companyId && s.MopProfileId == mopProfileId,
            ct
        );

        if (!mopCompanyExists)
        {
            var mopCompany = new MopProfileCompany
            {
                CompanyId = companyId,
                MopProfileId = mopProfileId,
                CreatedById = accountId
            };

            await context.MopProfileCompanies.AddAsync(mopCompany, ct);
        }
    }

    private async Task<Guid> SetMopProfileAsync(Guid accountId, string name, CancellationToken ct)
    {
        var profile = await context.MopProfiles.SingleOrDefaultAsync(
            s => s.AccountId == accountId,
            ct
        );

        if (profile == null)
        {
            profile = new MopProfile
            {
                AccountId = accountId,
                Name = name,
                Email = $"{name.Trim(' ')}@tsp.dev", //TODO check and remove
                CreatedById = accountId
            };

            await context.MopProfiles.AddAsync(profile, ct);

            return profile.MopProfileId;
        }

        profile.Name = name;
        profile.LastModifiedById = accountId;

        return profile.MopProfileId;
    }

    private bool ValidateUserRoles(Guid accountId, string name, List<RoleClaimModel> roles)
    {
        if (roles.Count == 0)
        {
            logger.LogWarning("User {Name} {AccountId} has no roles", name, accountId);
            return false;
        }

        if (roles.Count == 1 && roles.Select(s => s.RoleType).Contains(Enums.MedicsRoleType.App))
        {
            return true;
        }

        var mopRoles = new List<Enums.MedicsRoleType>
        {
            Enums.MedicsRoleType.Mop,
            Enums.MedicsRoleType.OtpUser
        };
        var officerRoles = new List<Enums.MedicsRoleType>
        {
            Enums.MedicsRoleType.AssignmentOfficer,
            Enums.MedicsRoleType.VerificationOfficer,
            Enums.MedicsRoleType.EvaluationOfficer,
            Enums.MedicsRoleType.SupportingOfficer,
            Enums.MedicsRoleType.ApprovingOfficer,
            Enums.MedicsRoleType.ReadOnlyOfficer,
            Enums.MedicsRoleType.ProductOwner,
        };

        var hasMopRoles = roles.Select(s => s.RoleType).ToList().Exists(mopRoles.Contains);
        var hasOfficerRoles = roles.Select(s => s.RoleType).ToList().Exists(officerRoles.Contains);

        if (hasMopRoles && hasOfficerRoles)
        {
            logger.LogWarning(
                "User {Name} {AccountId} has both MOP roles and Officer roles",
                name,
                accountId
            );
            return false;
        }

        if (hasMopRoles || hasOfficerRoles)
        {
            return true;
        }

        logger.LogWarning(
            "User {Name} {AccountId} has roles that are neither MOP roles nor Officer roles",
            name,
            accountId
        );
        return false;
    }

    private static List<RoleClaimModel> GetRoles(Dictionary<string, IEnumerable<string>> claims)
    {
        var roles = claims.GetValueOrDefault(ShareHttpParams.CommonClaims.Role)?.ToList();

        var medicRoles = new List<RoleClaimModel>();

        if (roles == null)
        {
            return medicRoles;
        }

        medicRoles = roles.GetRoleClaims();

        return medicRoles;
    }
}
