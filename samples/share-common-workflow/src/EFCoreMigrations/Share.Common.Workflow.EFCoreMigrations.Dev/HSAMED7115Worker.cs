using System;
using System.Runtime.ConstrainedExecution;
using System.Text.Json;
using System.Text.Json.Serialization;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Share.Common.Workflow.Core.Application.Services;
using Share.Common.Workflow.Core.Application.TaskModels;
using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Entities;
using Share.Common.Workflow.Core.Infrastructure.Services;
using Share.Lib.Abstractions.Common.Interfaces;
using Share.Lib.Bootstrap.Api.Core.Entities;

namespace Share.Common.Workflow.EFCoreMigrations.Dev;

#pragma warning disable S101 // Types should be named in PascalCase
public class HSAMED7115Worker(IServiceProvider provider, IHostApplicationLifetime lifetime)
#pragma warning restore S101 // Types should be named in PascalCase
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var scope = provider.CreateScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<HSAMED7115Worker>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var dateTime = scope.ServiceProvider.GetRequiredService<IDateTime>();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        logger.LogInformation("HSAMED7115Worker running at: {Time}", dateTime.Now);
        await AddAppsAsync(configuration, dbContext, stoppingToken);

        var applicationIdStrings = configuration.GetSection("ApplicationIds").Get<List<string>>()!;

        var appIds = applicationIdStrings.Select(id => Guid.Parse(id)).ToHashSet();

        await RouteApplicationFromAcceptForEvaluationToEvaluationRouteAsync(
            appIds,
            dbContext,
            dateTime,
            logger,
            stoppingToken
        );

        logger.LogInformation("HSAMED7115Worker completed at: {Time}", dateTime.Now);

        lifetime.StopApplication();
    }

    private static async Task AddAppsAsync(
        IConfiguration configuration,
        WorkflowDbContext dbContext,
        CancellationToken stoppingToken
    )
    {
        var workerServiceId = configuration.GetValue<string>("WorkerServiceId")!;

        var apps = new List<Account>
        {
            new() { AccountId = Guid.Parse(workerServiceId), Name = "Workflow EF Migrations App" }
        };

        var accounts = await dbContext.Accounts.ToListAsync(stoppingToken);

        foreach (
            var app in apps.Where(app => accounts.TrueForAll(a => a.AccountId != app.AccountId))
        )
        {
            await dbContext.Accounts.AddAsync(app, stoppingToken);
        }

        await dbContext.SaveChangesAsync(stoppingToken);
    }

    private static async Task RouteApplicationFromAcceptForEvaluationToEvaluationRouteAsync(
        HashSet<Guid> applicationIds,
        WorkflowDbContext dbContext,
        IDateTime dateTime,
        ILogger<HSAMED7115Worker> logger,
        CancellationToken ct
    )
    {
        var currentDate = dateTime.Now;

        foreach (var applicationId in applicationIds)
        {
            var stageAction = await dbContext
                .AppWorkflowLevelStageActions.Include(a => a.AppWorkflowLevelStage)
                .ThenInclude(c => c.AppWorkflowLevel)
                .ThenInclude(c => c.WorkflowLevel)
                .Include(a => a.AppWorkflowLevelStage)
                .ThenInclude(a => a.Stage)
                .Where(w =>
                    w.IsNotDeleted
                    && w.AppWorkflowLevelStage.AppWorkflowLevel.AppWorkflow.ApplicationId
                        == applicationId
                    && w.AppWorkflowLevelStage.AppWorkflowLevel.AppWorkflow.Status
                        == Enums.WorkflowStatus.InProgress
                    && w.Decision == Enums.WorkflowDecision.Pending
                    && w.AppWorkflowLevelStage.Stage.StageIdentifier
                        == Enums.WorkflowStageIdentifier.AcceptForEvaluation
                    && w.AppWorkflowLevelStage.AppWorkflowLevel.WorkflowLevel.WorkflowRole
                        == Enums.WorkflowRole.EvaluationOfficer
                )
                .SingleOrDefaultAsync(ct);

            if (stageAction == null)
            {
                logger.LogWarning(
                    "Application with app id {AppId} is currently not active in Accept for Evaluation route",
                    applicationId
                );

                continue;
            }

            var currentLevel = stageAction
                .AppWorkflowLevelStage
                .AppWorkflowLevel
                .WorkflowLevel
                .Level;
            var currentStage = stageAction.AppWorkflowLevelStage.Stage.Stage;

            stageAction.Decision = Enums.WorkflowDecision.Approve;
            stageAction.DecisionMadeAt = currentDate;

            stageAction.AppWorkflowLevelStage.IsActive = false;

            var nextStage = await dbContext
                .AppWorkflowLevelStages.Include(a => a.AppWorkflowLevel)
                .Where(w =>
                    w.AppWorkflowLevel.AppWorkflow.ApplicationId == applicationId
                    && w.AppWorkflowLevel.AppWorkflow.Status == Enums.WorkflowStatus.InProgress
                    && w.Stage.Stage == currentStage + 1
                    && w.AppWorkflowLevel.WorkflowLevel.Level == currentLevel
                )
                .SingleAsync(ct);

            nextStage.IsActive = true;

            var newStageAction = new AppWorkflowLevelStageAction
            {
                AppWorkflowLevelStageId = nextStage.AppWorkflowLevelStageId,
                AppOfficerId = stageAction.AppOfficerId,
                Decision = Enums.WorkflowDecision.Pending,
                ApplicationCreatedAtValue = currentDate
            };

            await dbContext.AppWorkflowLevelStageActions.AddAsync(newStageAction, ct);

            logger.LogWarning(
                "Application with app id {AppId} is routed to Evaluation route",
                applicationId
            );
        }

        await dbContext.SaveChangesAsync(ct);
    }
}
