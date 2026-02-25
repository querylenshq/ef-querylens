using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Entities;
using Share.Common.Workflow.Core.Infrastructure.Interfaces;
using Share.Common.Workflow.Core.Resources;
using Share.Lib.Abstractions.Common.Interfaces;
using Share.Lib.Bootstrap.Api.Core.Entities;

namespace Share.Common.Workflow.Core.Application.Services;

public class ApplicationWorkflowService(
    IWorkflowDbContext dbContext,
    IDateTime dateTime,
    AccessAuthApiService accessAuthApiService,
    ILogger<ApplicationWorkflowService> logger
)
{
    public async Task<TResult?> GetAppWorkflowByIdAsync<TResult>(
        Guid applicationId,
        Expression<Func<AppWorkflow, TResult>> expression,
        CancellationToken ct
    )
    {
        return await dbContext
            .AppWorkflows.Where(w => w.ApplicationId == applicationId && w.IsNotDeleted)
            .Select(expression)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<List<TResult>> GetAppWorkflowRemarksByApplicationIdAsync<TResult>(
        Guid applicationId,
        Expression<Func<AppWorkflowLevelStageActionRemark, TResult>> expression,
        CancellationToken ct
    )
    {
        return await dbContext
            .AppWorkflowLevelStageActionRemarks.AsNoTracking()
            .Where(w => w.IsNotDeleted)
            .Where(w => w.ApplicationId == applicationId)
            .Select(expression)
            .ToListAsync(ct);
    }

    /// <summary>
    /// As 20250910, only CN is allowed to replace the workflow
    /// </summary>
    public async Task<Guid> CreateApplicationWorkflowAndReplaceIfExistsAsync(
        Guid applicationId,
        Guid workflowId,
        CancellationToken ct
    )
    {
        await RemoveCurrentAppWorkflowAsync(applicationId, ct);

        Guid appWorkflowId = await CreateApplicationWorkflowAsync(applicationId, workflowId, ct);

        return appWorkflowId;
    }

    public async Task<Guid> CreateApplicationWorkflowAsync(
        Guid applicationId,
        Guid workflowId,
        CancellationToken ct
    )
    {
        var applicationWorkflow = new AppWorkflow
        {
            ApplicationId = applicationId,
            WorkflowId = workflowId,
            Status = Enums.WorkflowStatus.InProgress
        };

        dbContext.AppWorkflows.Add(applicationWorkflow);

        await SetApplicationWorkflowLevelsAsync(applicationWorkflow.AppWorkflowId, workflowId, ct);

        await dbContext.SaveChangesAsync(ct);

        return applicationWorkflow.AppWorkflowId;
    }

    private async Task RemoveCurrentAppWorkflowAsync(Guid applicationId, CancellationToken ct)
    {
        AppWorkflow? currentAppWorkflow = await dbContext
            .AppWorkflows.Include(i => i.Levels.Where(w => w.IsNotDeleted))
            .ThenInclude(ti => ti.Stages.Where(w => w.IsNotDeleted))
            .ThenInclude(ti2 => ti2.Actions.Where(w => w.IsNotDeleted))
            .Include(i => i.Levels.Where(w => w.IsNotDeleted))
            .ThenInclude(ti => ti.Stages.Where(w => w.IsNotDeleted))
            .ThenInclude(ti2 => ti2.PrivilegeActions.Where(w => w.IsNotDeleted))
            .Where(w => w.IsNotDeleted)
            .Where(w => w.ApplicationId == applicationId)
            .FirstOrDefaultAsync(ct);

        if (currentAppWorkflow == null)
        {
            return;
        }

        currentAppWorkflow.IsDeleted = true;

        foreach (AppWorkflowLevel appWorkflowLevel in currentAppWorkflow.Levels)
        {
            appWorkflowLevel.IsDeleted = true;
            foreach (AppWorkflowLevelStage appWorkflowLevelStage in appWorkflowLevel.Stages)
            {
                appWorkflowLevelStage.IsDeleted = true;

                foreach (
                    AppWorkflowLevelStageAction appWorkflowLevelStageAction in appWorkflowLevelStage.Actions
                )
                {
                    appWorkflowLevelStageAction.IsDeleted = true;
                }

                foreach (
                    AppWorkflowLevelStagePrivilegeAction appWorkflowLevelStagePrivilegeAction in appWorkflowLevelStage.PrivilegeActions
                )
                {
                    appWorkflowLevelStagePrivilegeAction.IsDeleted = true;
                }
            }
        }
    }

    private async Task SetApplicationWorkflowLevelsAsync(
        Guid applicationWorkflowId,
        Guid workflowId,
        CancellationToken ct
    )
    {
        var workflowLevels = await dbContext
            .WorkflowLevels.Where(wl => wl.WorkflowId == workflowId && wl.IsNotDeleted)
            .Select(s => new
            {
                s.WorkflowLevelId,
                StageIds = s
                    .Stages.Where(st => st.IsNotDeleted)
                    .Select(st => st.WorkflowLevelStageId)
                    .ToList()
            })
            .ToListAsync(ct);

        foreach (var workflowLevel in workflowLevels)
        {
            var applicationWorkflowLevel = new AppWorkflowLevel
            {
                AppWorkflowId = applicationWorkflowId,
                WorkflowLevelId = workflowLevel.WorkflowLevelId,
            };
            await dbContext.AppWorkflowLevels.AddAsync(applicationWorkflowLevel, ct);

            foreach (var stageId in workflowLevel.StageIds)
            {
                var applicationWorkflowLevelStage = new AppWorkflowLevelStage
                {
                    AppWorkflowLevelId = applicationWorkflowLevel.AppWorkflowLevelId,
                    IsActive = false,
                    StageId = stageId,
                };

                await dbContext.AppWorkflowLevelStages.AddAsync(applicationWorkflowLevelStage, ct);
            }
        }
    }

    public async Task AssignWorkflowStageAsync(
        Guid applicationId,
        int stage,
        int level,
        Guid? assigneeAccountId,
        Guid? assignerAccountId,
        string? remarks,
        CancellationToken ct
    )
    {
        Guid? assigneeAppOfficerId =
            assigneeAccountId != null
                ? await GetOrCreateApplicationOfficerAsync(
                    applicationId,
                    assigneeAccountId.Value,
                    ct
                )
                : null;

        var isSystemAssigned = assignerAccountId == null;

        var assignerAppOfficerId = assignerAccountId;

        if (!isSystemAssigned)
        {
            // This condition is important so that there is no duplicate application officer creation
            assignerAppOfficerId =
                assigneeAccountId != assignerAccountId
                    ? await GetOrCreateApplicationOfficerAsync(
                        applicationId,
                        assignerAccountId!.Value,
                        ct
                    )
                    : assigneeAppOfficerId!.Value;
        }

        var applicationWorkflowLevelStage = await dbContext
            .AppWorkflowLevelStages.Include(a => a.AppWorkflowLevel)
            .Include(a =>
                a.Actions.Where(w => w.IsNotDeleted && w.Decision == Enums.WorkflowDecision.Pending)
            )
            .Where(w =>
                w.IsNotDeleted
                && w.AppWorkflowLevel.IsNotDeleted
                && w.AppWorkflowLevel.AppWorkflow.IsNotDeleted
                && w.AppWorkflowLevel.AppWorkflow.ApplicationId == applicationId
                && w.AppWorkflowLevel.AppWorkflow.Status == Enums.WorkflowStatus.InProgress
                && w.AppWorkflowLevel.WorkflowLevel.IsNotDeleted
                && w.AppWorkflowLevel.WorkflowLevel.Level == level
                && w.Stage.IsNotDeleted
                && w.Stage.Stage == stage
            )
            .SingleAsync(ct);

        var currentActiveLevelStage = await dbContext
            .AppWorkflowLevelStages.Include(a => a.AppWorkflowLevel)
            .Where(w =>
                w.IsNotDeleted
                && w.IsActive
                && w.AppWorkflowLevel.IsNotDeleted
                && w.AppWorkflowLevel.IsActive
                && w.AppWorkflowLevel.AppWorkflow.IsNotDeleted
                && w.AppWorkflowLevel.AppWorkflow.ApplicationId == applicationId
                && w.AppWorkflowLevel.AppWorkflow.Status == Enums.WorkflowStatus.InProgress
            )
            .SingleOrDefaultAsync(ct);

        if (currentActiveLevelStage != null)
        {
            if (
                currentActiveLevelStage.AppWorkflowLevelStageId
                != applicationWorkflowLevelStage.AppWorkflowLevelStageId
            )
            {
                currentActiveLevelStage.IsActive = false;
            }

            if (
                currentActiveLevelStage.AppWorkflowLevelId
                != applicationWorkflowLevelStage.AppWorkflowLevelId
            )
            {
                currentActiveLevelStage.AppWorkflowLevel.IsActive = false;
            }
        }

        applicationWorkflowLevelStage.IsActive = true;
        applicationWorkflowLevelStage.AppWorkflowLevel.IsActive = true;

        var previousAction = applicationWorkflowLevelStage.Actions.SingleOrDefault();

        if (previousAction == null || previousAction.AppOfficerId != assigneeAppOfficerId)
        {
            if (previousAction != null)
            {
                previousAction.Decision = Enums.WorkflowDecision.UnTag;
                previousAction.DecisionMadeAt = dateTime.Now;

                if (assigneeAppOfficerId == null)
                {
                    previousAction.Remarks = remarks;
                    await AddAppWorkflowLevelStageActionRemarkAsync(
                        previousAction,
                        applicationId,
                        ct
                    );
                }
            }

            if (assigneeAppOfficerId != null)
            {
                if (!isSystemAssigned)
                {
                    var newAssignStageAction = new AppWorkflowLevelStageAction
                    {
                        AppWorkflowLevelStageId =
                            applicationWorkflowLevelStage.AppWorkflowLevelStageId,
                        AppOfficerId = assignerAppOfficerId!.Value,
                        Decision = Enums.WorkflowDecision.Assign,
                        DecisionMadeAt = dateTime.Now,
                        Remarks = remarks,
                        ApplicationCreatedAtValue = dateTime.Now
                    };

                    await dbContext.AppWorkflowLevelStageActions.AddAsync(newAssignStageAction, ct);

                    await AddAppWorkflowLevelStageActionRemarkAsync(
                        newAssignStageAction,
                        applicationId,
                        ct
                    );
                }

                var newStageAction = new AppWorkflowLevelStageAction
                {
                    AppWorkflowLevelStageId = applicationWorkflowLevelStage.AppWorkflowLevelStageId,
                    AppOfficerId = assigneeAppOfficerId.Value,
                    Decision = Enums.WorkflowDecision.Pending,
                    ApplicationCreatedAtValue = dateTime.Now
                };

                await dbContext.AppWorkflowLevelStageActions.AddAsync(newStageAction, ct);
            }
        }

        await dbContext.SaveChangesAsync(ct);
    }

    private async Task<Guid> GetOrCreateApplicationOfficerAsync(
        Guid applicationId,
        Guid accountId,
        CancellationToken ct
    )
    {
        var workflow = await dbContext
            .AppWorkflows.Where(w => w.IsNotDeleted)
            .Where(w =>
                w.ApplicationId == applicationId && w.Status == Enums.WorkflowStatus.InProgress
            )
            .SingleAsync(ct);

        var officer = await dbContext
            .ApplicationOfficers.Where(w => w.IsNotDeleted)
            .Where(w => w.OfficerId == accountId && w.AppWorkflowId == workflow.AppWorkflowId)
            .SingleOrDefaultAsync(ct);

        if (officer == null)
        {
            var officerAccount = await dbContext
                .Accounts.Where(w => w.IsNotDeleted)
                .Where(w => w.AccountId == accountId)
                .SingleOrDefaultAsync(ct);

            if (officerAccount == null)
            {
                var accountResp = await accessAuthApiService.GetAccountDetailsAsync(accountId, ct);

                officerAccount = new Account
                {
                    AccountId = accountId,
                    Name = accountResp?.Name ?? accountId.ToString(),
                    CreatedById = accountId
                };

                await dbContext.Accounts.AddAsync(officerAccount, ct);
            }

            logger.LogInformation("Assign to officer with officer ID {0}", accountId);
            officer = new ApplicationOfficer
            {
                AppWorkflowId = workflow.AppWorkflowId,
                OfficerId = accountId,
            };

            await dbContext.ApplicationOfficers.AddAsync(officer, ct);
            await dbContext.SaveChangesAsync(ct);
        }

        return officer.ApplicationOfficerId;
    }

    public async Task PostWorkflowStageDecisionAsync(
        PostWorkflowStageDecisionTaskModel model,
        CancellationToken ct
    )
    {
        (var currentLevel, var currentStage) = await SetStageActionDecisionAsync(model, ct);

        if (
            !model.IsFinalStage
            && model.Decision != Enums.WorkflowDecision.InstantApprove
            && model.Decision != Enums.WorkflowDecision.InstantReject
        )
        {
            await SetNextStageAsync(model, currentLevel, currentStage, ct);
        }

        await dbContext.SaveChangesAsync(ct);
    }

    private async Task SetNextStageAsync(
        PostWorkflowStageDecisionTaskModel model,
        int currentLevel,
        int currentStage,
        CancellationToken ct
    )
    {
        if (
            currentLevel > model.NextLevel
            || (currentLevel == model.NextLevel && currentStage > model.NextStage)
        )
        {
            var previousActions = await dbContext
                .AppWorkflowLevelStageActions.Where(w =>
                    w.IsNotDeleted
                    && w.AppWorkflowLevelStage.IsNotDeleted
                    && w.AppWorkflowLevelStage.AppWorkflowLevel.IsNotDeleted
                    && w.AppWorkflowLevelStage.AppWorkflowLevel.AppWorkflow.IsNotDeleted
                    && w.AppWorkflowLevelStage.AppWorkflowLevel.AppWorkflow.ApplicationId
                        == model.ApplicationId
                    && w.AppWorkflowLevelStage.AppWorkflowLevel.AppWorkflow.Status
                        == Enums.WorkflowStatus.InProgress
                    && w.AppWorkflowLevelStage.AppWorkflowLevel.WorkflowLevel.IsNotDeleted
                    && w.AppWorkflowLevelStage.AppWorkflowLevel.WorkflowLevel.Level
                        >= model.NextLevel
                    && w.AppWorkflowLevelStage.Stage.Stage >= model.NextStage
                )
                .ToListAsync(ct);

            foreach (var action in previousActions)
            {
                action.IsDeleted = true;
            }
        }

        var stage = await dbContext
            .AppWorkflowLevelStages.Include(a => a.AppWorkflowLevel)
            .Where(w =>
                w.IsNotDeleted
                && w.AppWorkflowLevel.IsNotDeleted
                && w.AppWorkflowLevel.AppWorkflow.IsNotDeleted
                && w.AppWorkflowLevel.AppWorkflow.ApplicationId == model.ApplicationId
                && w.AppWorkflowLevel.AppWorkflow.Status == Enums.WorkflowStatus.InProgress
                && w.AppWorkflowLevel.WorkflowLevel.IsNotDeleted
                && w.AppWorkflowLevel.WorkflowLevel.Level == model.NextLevel
                && w.Stage.IsNotDeleted
                && w.Stage.Stage == model.NextStage
            )
            .SingleAsync(ct);

        stage.IsActive = true;
        stage.AppWorkflowLevel.IsActive = true;

        if (model.AccountId.HasValue)
        {
            var appOfficerId = await GetOrCreateApplicationOfficerAsync(
                model.ApplicationId,
                model.AccountId.Value,
                ct
            );
            var stageAction = new AppWorkflowLevelStageAction
            {
                AppWorkflowLevelStageId = stage.AppWorkflowLevelStageId,
                AppOfficerId = appOfficerId,
                Decision = Enums.WorkflowDecision.Pending,
                ApplicationCreatedAtValue = dateTime.Now
            };

            await dbContext.AppWorkflowLevelStageActions.AddAsync(stageAction, ct);
        }
    }

    private async Task AddAppWorkflowLevelStageActionRemarkAsync(
        AppWorkflowLevelStageAction stageAction,
        Guid applicationId,
        CancellationToken ct
    )
    {
        if (!string.IsNullOrWhiteSpace(stageAction.Remarks))
        {
            var newAssignStageActionRemark = new AppWorkflowLevelStageActionRemark
            {
                AppOfficerId = stageAction.AppOfficerId,
                ApplicationId = applicationId,
                AppWorkflowLevelStageActionId = stageAction.AppWorkflowLevelStageActionId,
                CompletedAt = stageAction.DecisionMadeAt,
                Remarks = stageAction.Remarks,
                StartedAt = stageAction.CreatedAt
            };
            await dbContext.AppWorkflowLevelStageActionRemarks.AddAsync(
                newAssignStageActionRemark,
                ct
            );
        }
    }

    private async Task<(int, int)> SetStageActionDecisionAsync(
        PostWorkflowStageDecisionTaskModel model,
        CancellationToken ct
    )
    {
        var stageAction = await dbContext
            .AppWorkflowLevelStageActions.Include(a => a.AppWorkflowLevelStage)
            .ThenInclude(c => c.AppWorkflowLevel)
            .ThenInclude(c => c.WorkflowLevel)
            .Include(a => a.AppWorkflowLevelStage.Stage)
            .Where(w =>
                w.IsNotDeleted
                && w.Decision == Enums.WorkflowDecision.Pending
                && w.AppWorkflowLevelStage.IsNotDeleted
                && w.AppWorkflowLevelStage.AppWorkflowLevel.IsNotDeleted
                && w.AppWorkflowLevelStage.AppWorkflowLevel.AppWorkflow.IsNotDeleted
                && w.AppWorkflowLevelStage.AppWorkflowLevel.AppWorkflow.ApplicationId
                    == model.ApplicationId
                && w.AppWorkflowLevelStage.AppWorkflowLevel.AppWorkflow.Status
                    == Enums.WorkflowStatus.InProgress
            )
            .SingleAsync(ct);

        var currentLevel = stageAction.AppWorkflowLevelStage.AppWorkflowLevel.WorkflowLevel.Level;
        var currentStage = stageAction.AppWorkflowLevelStage.Stage.Stage;

        stageAction.Decision = model.Decision;
        stageAction.DecisionMadeAt = model.DecisionDate;
        stageAction.Remarks = model.Remarks;

        stageAction.AppWorkflowLevelStage.IsActive = false;
        stageAction.AppWorkflowLevelStage.AppWorkflowLevel.IsActive = false;

        await AddAppWorkflowLevelStageActionRemarkAsync(stageAction, model.ApplicationId, ct);

        return (currentLevel, currentStage);
    }

    public async Task CompleteApplicationWorkflowAsync(
        Guid applicationId,
        DateTime completedAt,
        CancellationToken ct
    )
    {
        var applicationWorkflow = await dbContext
            .AppWorkflows.Where(w => w.IsNotDeleted)
            .Where(w =>
                w.ApplicationId == applicationId && w.Status == Enums.WorkflowStatus.InProgress
            )
            .SingleAsync(ct);

        applicationWorkflow.Status = Enums.WorkflowStatus.Completed;
        applicationWorkflow.CompletedAt = completedAt;

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<List<string>> ValidateCompleteApplicationWorkflowAsync(
        Guid applicationId,
        CancellationToken ct
    )
    {
        var errors = new List<string>();

        var workflow = await GetAppWorkflowByIdAsync(
            applicationId,
            appWorkflow => new { appWorkflow.AppWorkflowId, appWorkflow.Status },
            ct
        );

        if (workflow is not { Status: Enums.WorkflowStatus.InProgress })
        {
            errors.Add(WorkflowValidation.ActiveWorkflowNotExists);

            return errors;
        }

        List<Guid> appWorkflowLevelIds = await dbContext
            .AppWorkflowLevels.AsNoTracking()
            .Where(w => w.IsNotDeleted)
            .Where(w => w.IsActive)
            .Where(w => w.AppWorkflowId == workflow.AppWorkflowId)
            .Select(s => s.AppWorkflowLevelId)
            .ToListAsync(ct);

        List<Guid> appWorkflowLevelStageIds = await dbContext
            .AppWorkflowLevelStages.AsNoTracking()
            .Where(w => w.IsNotDeleted)
            .Where(w => w.IsActive)
            .Where(w => appWorkflowLevelIds.Contains(w.AppWorkflowLevelId))
            .Select(s => s.AppWorkflowLevelStageId)
            .ToListAsync(ct);

        bool hasPendingActionWorkflow = await dbContext
            .AppWorkflowLevelStageActions.AsNoTracking()
            .AnyAsync(
                w =>
                    w.IsNotDeleted
                    && appWorkflowLevelStageIds.Contains(w.AppWorkflowLevelStageId)
                    && w.Decision == Enums.WorkflowDecision.Pending,
                ct
            );

        if (hasPendingActionWorkflow)
        {
            errors.Add(WorkflowValidation.AppWorkflowPendingLevels);

            return errors;
        }

        return errors;
    }
}

public class PostWorkflowStageDecisionTaskModel
{
    public Guid ApplicationId { get; set; }

    public Enums.WorkflowDecision Decision { get; set; }

    public DateTime DecisionDate { get; set; }

    public string? Remarks { get; set; }

    public int NextLevel { get; set; }

    public int NextStage { get; set; }

    public Guid? AccountId { get; set; }
    public bool IsFinalStage { get; set; }
}
