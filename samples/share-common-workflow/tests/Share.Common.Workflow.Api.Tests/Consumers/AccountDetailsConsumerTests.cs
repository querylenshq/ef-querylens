using Microsoft.EntityFrameworkCore;
using Share.Common.AccessControl.MessageContracts;
using Share.Common.AccessControl.MessageContracts.MessageContractEnums;
using Share.Common.Workflow.Api.Tests.Setup;
using Share.Common.Workflow.Core.Application.Services;
using Share.Common.Workflow.Core.Application.TaskModels;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Shouldly;
using static Share.Common.Workflow.Core.Domain.Enums;

namespace Share.Common.Workflow.Api.Tests.Consumers;

public class AccountDetailsConsumerTests(ApiApplicationFactory factory)
    : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task Consume_WhenInternetUserDoesNotExist_CreateNewUser()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        var message = new AccountDetailsMessageContract
        {
            AccountId = Guid.NewGuid(),
            Name = "TestAccountName",
            InternetUserProfile = new()
            {
                Name = "TestUserName",
                IdentificationNoHash = "TestIdentificationNoHash",
                Designation = "TestDesignation",
                EmailAddress = "TestUserEmail",
                InternetUserProfileId = Guid.NewGuid(),
            },
            AccountStatus = MessageContractEnums.AccountStatus.Active,
            AccountType = MessageContractEnums.AccountType.InternetUser,
            CamsUserActionType = MessageContractEnums.CamsUserActionType.Sync,
            MessageRequestType = MessageContractEnums.MessageRequestType.Share,
        };

        await BusControlHelper.WaitForStartAsync(ct);

        var endpoint =
            await TestHarness.Bus.GetPublishSendEndpoint<AccountDetailsMessageContract>();

        // Act
        await endpoint.Send(message, ct);

        // Assert
        await BusControlHelper.WaitForConsumptionWithTimeoutAsync<AccountDetailsMessageContract>(
            TimeSpan.FromSeconds(10),
            x => x.Context.Message.AccountId == message.AccountId
        );

        bool isUserCreated = await InternetUserExistsAsync(message.AccountId);
        isUserCreated.ShouldBeTrue();
    }

    [Fact]
    public async Task Consume_WhenAccountExists_UpdatesExistingAccount()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        string accountName = "TestAccountName";
        string expectedAccountName = "TestAccountNameUpdated";
        Guid accountId = await PrepareAccountAsync(accountName, ct);

        var message = new AccountDetailsMessageContract
        {
            AccountId = accountId,
            Name = expectedAccountName,
            AccountStatus = MessageContractEnums.AccountStatus.Active,
            AccountType = MessageContractEnums.AccountType.InternetUser,
            CamsUserActionType = MessageContractEnums.CamsUserActionType.Sync,
            MessageRequestType = MessageContractEnums.MessageRequestType.Share,
        };

        await BusControlHelper.WaitForStartAsync(ct);

        var endpoint =
            await TestHarness.Bus.GetPublishSendEndpoint<AccountDetailsMessageContract>();

        // Act
        await endpoint.Send(message, ct);

        // Assert
        await BusControlHelper.WaitForConsumptionWithTimeoutAsync<AccountDetailsMessageContract>(
            TimeSpan.FromSeconds(10),
            x => x.Context.Message.AccountId == message.AccountId
        );

        var account = await GetAccountAsync(accountId, ct);
        account.Name.ShouldBe(expectedAccountName);
    }

    private async Task<Guid> PrepareAccountAsync(string accountName, CancellationToken ct = default)
    {
        using var scope = Services.CreateScope();
        var accountService = scope.ServiceProvider.GetRequiredService<WorkflowAccountService>();

        var taskModel = new AccountDetailsTaskModel
        {
            AccountId = Guid.NewGuid(),
            Name = accountName,
            AccountType = AccountType.InternetUser,
            MessageRequestType = MessageRequestType.Share,
            CamsUserActionType = CamsUserActionType.Sync,
            AccountStatus = AccountStatus.Active,
        };

        taskModel.Roles.Add(
            new()
            {
                Name = "TestRoleName",
                RoleCode = "TestRoleCode",
                Description = "TestDescription",
                WorkflowApplicationType = WorkflowType.DealerLicenseNew,
            }
        );

        await accountService.SetAccountInformationAsync([taskModel], ct);

        return taskModel.AccountId;
    }

    [Fact]
    public async Task Consume_WhenIntranetUserDoesNotExist_CreateNewUser()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        var message = new AccountDetailsMessageContract
        {
            AccountId = Guid.NewGuid(),
            Name = "TestAccountName",
            IntranetUserProfile = new()
            {
                Name = "TestUserName",
                Designation = "TestDesignation",
                SoeId = "TestSoeId",
                UserEmail = "TestUserEmail@email.com",
                IntranetUserProfileId = Guid.NewGuid(),
            },
            AccountStatus = MessageContractEnums.AccountStatus.Active,
            AccountType = MessageContractEnums.AccountType.InternetUser,
            CamsUserActionType = MessageContractEnums.CamsUserActionType.Sync,
            MessageRequestType = MessageContractEnums.MessageRequestType.Share,
        };

        await BusControlHelper.WaitForStartAsync(ct);

        var endpoint =
            await TestHarness.Bus.GetPublishSendEndpoint<AccountDetailsMessageContract>();

        // Act
        await endpoint.Send(message, ct);

        // Assert
        await BusControlHelper.WaitForConsumptionWithTimeoutAsync<AccountDetailsMessageContract>(
            TimeSpan.FromSeconds(10),
            x => x.Context.Message.AccountId == message.AccountId
        );

        bool isUserCreated = await IntranetUserExistsAsync(message.AccountId);
        isUserCreated.ShouldBeTrue();
    }

    private Task<Account> GetAccountAsync(Guid accountId, CancellationToken ct)
    {
        return Db.Accounts.AsNoTracking().FirstAsync(a => a.AccountId == accountId, ct);
    }

    private Task<bool> InternetUserExistsAsync(Guid accountId)
    {
        return Db.MopProfiles.AsNoTracking().AnyAsync(o => o.AccountId == accountId);
    }

    private Task<bool> IntranetUserExistsAsync(Guid accountId)
    {
        return Db.OfficerProfiles.AsNoTracking().AnyAsync(o => o.AccountId == accountId);
    }
}
