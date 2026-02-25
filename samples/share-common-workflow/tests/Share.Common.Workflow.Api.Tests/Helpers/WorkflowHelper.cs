using Share.Common.Workflow.Core.Application.TaskModels;
using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Entities;
using Share.Common.Workflow.Core.Infrastructure.Services;

namespace Share.Common.Workflow.Api.Tests.Helpers;

internal class WorkflowHelper(WorkflowDbContext dbContext, IConfiguration configuration)
{
    public Task SeedWorkflowsAsync(CancellationToken cancellationToken = default)
    {
        var serviceAccountId = configuration.GetValue<Guid>("CurrentUserOptions:ServiceAccountId");

        return CreateNewDlWorkflowAsync(serviceAccountId, cancellationToken);
    }

    private Task CreateNewDlWorkflowAsync(
        Guid serviceAccountId,
        CancellationToken cancellationToken = default
    )
    {
        var workflow = new CreateWorkflowTaskModel
        {
            Name = "New Dealer License",
            WorkflowType = Enums.WorkflowType.DealerLicenseNew,
            Levels =
            [
                new CreateWorkflowTaskModel.CreateWorkflowLevelTaskModel
                {
                    Name = "Assignment Level",
                    WorkflowRole = Enums.WorkflowRole.AssignmentOfficer,
                    IsFinal = false,
                    Level = 1,
                    Stages =
                    [
                        new CreateWorkflowTaskModel.CreateWorkflowLevelStageTakModel
                        {
                            Name = "Assignment",
                            Stage = 1,
                            Privileges =
                            [
                                new CreateWorkflowTaskModel.CreateWorkflowLevelStagePrivilegeTaskModel
                                {
                                    PrivilegeType = Enums.WorkflowPrivilegeType.Approve,
                                    PrivilegeRequirementType = Enums
                                        .WorkflowPrivilegeRequirementType
                                        .RequiredToCompleteStage
                                }
                            ]
                        }
                    ]
                },
                new CreateWorkflowTaskModel.CreateWorkflowLevelTaskModel
                {
                    Name = "Evaluation Level",
                    WorkflowRole = Enums.WorkflowRole.EvaluationOfficer,
                    IsFinal = false,
                    Level = 2,
                    Stages =
                    [
                        new CreateWorkflowTaskModel.CreateWorkflowLevelStageTakModel
                        {
                            Name = "Evaluation",
                            Stage = 1,
                            Privileges =
                            [
                                new CreateWorkflowTaskModel.CreateWorkflowLevelStagePrivilegeTaskModel
                                {
                                    PrivilegeType = Enums.WorkflowPrivilegeType.Approve,
                                    PrivilegeRequirementType = Enums
                                        .WorkflowPrivilegeRequirementType
                                        .RequiredToCompleteStage
                                },
                                new CreateWorkflowTaskModel.CreateWorkflowLevelStagePrivilegeTaskModel
                                {
                                    PrivilegeType = Enums.WorkflowPrivilegeType.RaiseIr,
                                    PrivilegeRequirementType = Enums
                                        .WorkflowPrivilegeRequirementType
                                        .RequiredIfInitiated
                                },
                                new CreateWorkflowTaskModel.CreateWorkflowLevelStagePrivilegeTaskModel()
                                {
                                    PrivilegeType = Enums
                                        .WorkflowPrivilegeType
                                        .InitiateDeferredPayment,
                                    PrivilegeRequirementType = Enums
                                        .WorkflowPrivilegeRequirementType
                                        .RequiredToCompleteStage
                                }
                            ]
                        }
                    ]
                },
                new CreateWorkflowTaskModel.CreateWorkflowLevelTaskModel
                {
                    Name = "Approval Level",
                    WorkflowRole = Enums.WorkflowRole.ApprovingOfficer,
                    IsFinal = true,
                    Level = 3,
                    Stages =
                    [
                        new CreateWorkflowTaskModel.CreateWorkflowLevelStageTakModel
                        {
                            Name = "Approval",
                            Stage = 1,
                            Privileges =
                            [
                                new CreateWorkflowTaskModel.CreateWorkflowLevelStagePrivilegeTaskModel
                                {
                                    PrivilegeType = Enums.WorkflowPrivilegeType.Approve,
                                    PrivilegeRequirementType = Enums
                                        .WorkflowPrivilegeRequirementType
                                        .RequiredToCompleteStage
                                },
                                new CreateWorkflowTaskModel.CreateWorkflowLevelStagePrivilegeTaskModel
                                {
                                    PrivilegeType = Enums.WorkflowPrivilegeType.Reject,
                                    PrivilegeRequirementType = Enums
                                        .WorkflowPrivilegeRequirementType
                                        .Optional
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        return CreateWorkflowAsync(serviceAccountId, workflow, cancellationToken);
    }

    private async Task CreateWorkflowAsync(
        Guid serviceAccountId,
        CreateWorkflowTaskModel model,
        CancellationToken cancellationToken = default
    )
    {
        var workflow = new Core.Entities.Workflow
        {
            WorkflowId = ShareWorkflowApiConstants.DealerLicenseNewId,
            Name = model.Name,
            WorkflowType = model.WorkflowType,
            CreatedById = serviceAccountId
        };

        await dbContext.Workflows.AddAsync(workflow, cancellationToken);

        foreach (var level in model.Levels)
        {
            var workflowLevel = new WorkflowLevel
            {
                Level = level.Level,
                Name = level.Name,
                IsFinal = level.IsFinal,
                WorkflowRole = level.WorkflowRole,
                WorkflowId = workflow.WorkflowId,
                CreatedById = serviceAccountId
            };

            await dbContext.WorkflowLevels.AddAsync(workflowLevel, cancellationToken);

            foreach (var stage in level.Stages)
            {
                var workflowLevelStage = new WorkflowLevelStage
                {
                    Stage = stage.Stage,
                    Name = stage.Name,
                    WorkflowLevelId = workflowLevel.WorkflowLevelId,
                    CreatedById = serviceAccountId,
                    IsFinal = stage.IsFinal
                };

                await dbContext.WorkflowLevelStages.AddAsync(workflowLevelStage, cancellationToken);

                foreach (var privilege in stage.Privileges)
                {
                    var workflowLevelStagePrivilege = new WorkflowLevelStagePrivilege
                    {
                        StageId = workflowLevelStage.WorkflowLevelStageId,
                        PrivilegeType = privilege.PrivilegeType,
                        PrivilegeRequirementType = privilege.PrivilegeRequirementType,
                        CreatedById = serviceAccountId,
                    };

                    await dbContext.WorkflowLevelStagePrivileges.AddAsync(
                        workflowLevelStagePrivilege,
                        cancellationToken
                    );
                }
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
