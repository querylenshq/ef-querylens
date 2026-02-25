using System.Text.Json;
using Ardalis.GuardClauses;
using Microsoft.Extensions.Logging;
using Share.Common.AccessControl.Auth.Api.Client;
using Share.Common.AccessControl.Auth.Api.Client.Access.AccountDetail.Item;
using Share.Common.AccessControl.Auth.Api.Client.Models;

namespace Share.Common.Workflow.Core.Application.Services;

public class AccessAuthApiService(
    AccessAuthApiClient accessAuthApiClient,
    ILogger<AccessAuthApiService> logger
)
{
    public async Task<AccountDetails?> GetAccountDetailsAsync(Guid accountId, CancellationToken ct)
    {
        var authAccountDetail = await accessAuthApiClient
            .Access.AccountDetail[accountId]
            .GetWithExceptionHandlingAsync(cancellationToken: ct);

        Guard.Against.Null(authAccountDetail);

        if (
            authAccountDetail is { IsSuccess: not null }
            && authAccountDetail.IsSuccess.Value
            && authAccountDetail.AccountDetail != null
        )
        {
            return authAccountDetail.AccountDetail!;
        }

        logger.LogError(
            "Failed to get account detail {AccountId}; accountDetail.IsSuccess: {IsSuccess}; Errors: {Errors}",
            accountId,
            authAccountDetail?.IsSuccess,
            JsonSerializer.Serialize(authAccountDetail?.Errors)
        );
        return null;
    }
}
