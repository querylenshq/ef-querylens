using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Share.Common.Workflow.Api.Client;
using Share.Common.Workflow.Api.Tests.Helpers;
using Share.Common.Workflow.Core.Application.TaskModels;
using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Entities;
using Share.Common.Workflow.Core.Infrastructure.Services;
using Share.IntegrationTest.Abstractions.BaseIntegrationTest;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.Bootstrap.Api.Endpoints;
using Shouldly;

namespace Share.Common.Workflow.Api.Tests.Setup;

[Collection(nameof(ApiApplicationCollection))]
public class BaseIntegrationTest
    : ShareApiBaseIntegrationTest<
        ApiApplicationFactory,
        ApiTestMarker,
        WorkflowDbContext,
        WorkflowApiClient
    >
{
    protected BaseIntegrationTest(ApiApplicationFactory factory)
        : base(factory, resetDatabase: false)
    {
        TestHarness = Services.GetTestHarness();
        BusControlHelper = new BusControlHelper(Services, TestHarness);
    }

    internal readonly ITestHarness TestHarness;
    internal readonly BusControlHelper BusControlHelper;

    internal async Task CreateAccountsIfNotExistAsync(HashSet<Guid> accountIds)
    {
        Db.ChangeTracker.Clear();

        var existingAccountIds = (
            await Db
                .Accounts.AsNoTracking()
                .Where(a => accountIds.Contains(a.AccountId))
                .Select(s => s.AccountId)
                .ToListAsync()
        ).ToHashSet();
        var newAccounts = new List<Account>();

        foreach (var accountId in accountIds)
        {
            if (existingAccountIds.Contains(accountId))
            {
                continue;
            }

            var account = new Account
            {
                Name = "Test Account",
                AccountId = accountId,
                CreatedById = accountId
            };

            newAccounts.Add(account);
        }

        await Db.AddRangeAsync(newAccounts);

        await Db.SaveChangesAsync();
    }

    internal async Task<string?> GetAuthTokenAsync(Guid? accountId = null)
    {
        Db.ChangeTracker.Clear();

        await CreateAccountsIfNotExistAsync([accountId ?? Guid.NewGuid()]);

        var message = await Client.PostAsJsonAsync(
            "/workflows/dev/hmac",
            new TempLoginEndpointRequest { AccountId = accountId!.Value, AllPermission = true }
        );
        var res = await message.Content.ReadFromJsonAsync<TempLoginEndpointResponse>();

        res.ShouldNotBeNull();
        res.IsSuccess.ShouldBeTrue();
        Client.DefaultRequestHeaders.Add("Authorization", res.Token);

        return res.Token;
    }
}
