using MassTransit;
using Share.Common.AccessControl.MessageContracts;
using Share.Common.Workflow.Core.Application.Services;
using Share.Common.Workflow.Core.Application.TaskModels;
using static Share.Common.Workflow.Core.Domain.Constants;

namespace Share.Common.Workflow.Api.Consumers;

public class AccountDetailsConsumer(
    WorkflowAccountService accountService,
    ILogger<AccountDetailsConsumer> logger
) : IConsumer<Batch<AccountDetailsMessageContract>>
{
    public async Task Consume(ConsumeContext<Batch<AccountDetailsMessageContract>> context)
    {
        logger.LogInformation(
            "Received AccountDetailsMessageContract Batch: {CorrelationId} ",
            context.CorrelationId
        );

        var messages = context.Message.ToList();
        var messagesToConsume = RemoveDuplicates(messages);
        await ConsumeMessagesAsync(messagesToConsume, context.CancellationToken);
    }

    private async Task ConsumeMessagesAsync(
        List<ConsumeContext<AccountDetailsMessageContract>> messages,
        CancellationToken ct
    )
    {
        await accountService.SetAccountInformationAsync(
            messages
                .Select(message => new AccountDetailsTaskModel
                {
                    AccountDetailsMessageTransactionId = message
                        .Message
                        .AccountDetailsMessageTransactionId,
                    AccountId = message.Message.AccountId,
                    AccountStatus = (Core.Domain.Enums.AccountStatus)message.Message.AccountStatus,
                    AccountType = (Core.Domain.Enums.AccountType)message.Message.AccountType,
                    CamsUserActionType = (Core.Domain.Enums.CamsUserActionType)
                        message.Message.CamsUserActionType,
                    MessageRequestType = (Core.Domain.Enums.MessageRequestType)
                        message.Message.MessageRequestType,
                    Name = message.Message.Name,
                    InternetUserProfile =
                        message.Message.InternetUserProfile == null
                            ? null
                            : new AccountDetailsTaskModelInternetUserProfile
                            {
                                InternetUserProfileId = message
                                    .Message
                                    .InternetUserProfile
                                    .InternetUserProfileId,
                                Name = message.Message.InternetUserProfile.Name,
                                IdentificationNoHash = message
                                    .Message
                                    .InternetUserProfile
                                    .IdentificationNoHash,
                                Designation = message.Message.InternetUserProfile.Designation,
                                Companies = message
                                    .Message.InternetUserProfile.Companies.Select(
                                        company => new AccountDetailsTaskModelInternetUserProfileCompany
                                        {
                                            CompanyId = company.CompanyId,
                                            Name = company.Name,
                                            Uen = company.Uen,
                                            CompanyAddresses = company
                                                .CompanyAddresses.Select(
                                                    companyAddress => new AccountDetailsTaskModelInternetUserProfileCompanyCompanyAddress
                                                    {
                                                        CompanyAddressId =
                                                            companyAddress.CompanyAddressId,
                                                        PostalCode = companyAddress.PostalCode,
                                                        BlockHouseNo = companyAddress.BlockHouseNo,
                                                        FloorNo = companyAddress.FloorNo,
                                                        StreetName = companyAddress.StreetName,
                                                        BuildingName = companyAddress.BuildingName,
                                                        Other = companyAddress.Other,
                                                    }
                                                )
                                                .ToList(),
                                            SingaporeRegistered = company.SingaporeRegistered,
                                        }
                                    )
                                    .ToList(),
                                EmailAddress = message.Message.InternetUserProfile.EmailAddress,
                            },
                    IntranetUserProfile =
                        message.Message.IntranetUserProfile == null
                            ? null
                            : new AccountDetailsTaskModelIntranetUserProfile
                            {
                                IntranetUserProfileId = message
                                    .Message
                                    .IntranetUserProfile
                                    .IntranetUserProfileId,
                                Name = message.Message.IntranetUserProfile.Name,
                                Designation = message.Message.IntranetUserProfile.Designation,
                                SoeId = message.Message.IntranetUserProfile.SoeId,
                                UserEmail = message.Message.IntranetUserProfile.UserEmail,
                            },
                    Roles = message
                        .Message.Roles.Where(w =>
                            w.AppClientCode == WorkflowConstants.MedicsAppClientCode
                            || w.AppClientCode == WorkflowConstants.ShareAppClientCode
                        )
                        .Select(accountRole => new AccountDetailsTaskModelAccountRole
                        {
                            Description = accountRole.Description,
                            RoleCode = accountRole.RoleCode,
                            RoleId = accountRole.RoleId,
                            Name = accountRole.Name,
                            WorkflowApplicationType = (Core.Domain.Enums.WorkflowType?)
                                accountRole.WorkflowApplicationType
                        })
                        .ToList(),
                    Timestamp = message.Message.Timestamp,
                })
                .ToList(),
            ct
        );
    }

    private static List<ConsumeContext<AccountDetailsMessageContract>> RemoveDuplicates(
        IEnumerable<ConsumeContext<AccountDetailsMessageContract>> messages
    )
    {
        var messageContractDict =
            new Dictionary<Guid, ConsumeContext<AccountDetailsMessageContract>>();
        foreach (var message in messages)
        {
            if (!messageContractDict.ContainsKey(message.Message.AccountId))
            {
                messageContractDict.Add(message.Message.AccountId, message);
            }
            else if (
                messageContractDict.Any(w =>
                    w.Key == message.Message.AccountId
                    && w.Value.Message.Timestamp < message.Message.Timestamp
                )
            )
            {
                messageContractDict[message.Message.AccountId] = message;
            }
        }

        return [.. messageContractDict.Values];
    }
}
