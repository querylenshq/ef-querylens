using MassTransit;
using Share.Common.Workflow.Core.Infrastructure.Services;
using Share.Lib.Bootstrap.Api.Core.Entities;

namespace Share.Common.Workflow.Api.Tests.Helpers;

public class AuthHelper(WorkflowDbContext dbContext, IConfiguration configuration)
{
    public async Task SeedServiceAccountAsync(CancellationToken cancellationToken = default)
    {
        var accountId = configuration.GetValue<Guid>("CurrentUserOptions:ServiceAccountId");

        var account = new Account
        {
            Name = "Test Account",
            AccountId = accountId,
            CreatedById = accountId
        };

        await dbContext.Accounts.AddAsync(account);

        await dbContext.SaveChangesAsync();
    }
}
