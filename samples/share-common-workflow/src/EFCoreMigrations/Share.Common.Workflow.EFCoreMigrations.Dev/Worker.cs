using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Share.Common.Workflow.Core.Application.TaskModels;
using Share.Common.Workflow.Core.Domain;
using Share.Common.Workflow.Core.Entities;
using Share.Common.Workflow.Core.Infrastructure.Services;
using Share.Lib.Abstractions.Common.Interfaces;
using Share.Lib.Bootstrap.Api.Core.Entities;

namespace Share.Common.Workflow.EFCoreMigrations.Dev;

public class Worker(IServiceProvider provider, IHostApplicationLifetime lifetime)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var scope = provider.CreateScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Worker>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var dateTime = scope.ServiceProvider.GetRequiredService<IDateTime>();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        logger.LogInformation("Worker running at: {Time}", dateTime.Now);

        await MigrateDatabaseAsync(dbContext, stoppingToken);

        await AddAppsAsync(configuration, dbContext, stoppingToken);

        await SeedWorkflowsAsync(dbContext, logger, stoppingToken);

        logger.LogInformation("Worker completed at: {Time}", dateTime.Now);

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

    static async Task MigrateDatabaseAsync(
        WorkflowDbContext dbContext,
        CancellationToken stoppingToken
    )
    {
        await dbContext.Database.MigrateAsync(stoppingToken);
    }

    static async Task SeedWorkflowsAsync(
        WorkflowDbContext dbContext,
        ILogger<Worker> logger,
        CancellationToken stoppingToken
    )
    {
        const string directory = "./workflows";

        if (!Directory.Exists(directory))
        {
            logger.LogWarning("No workflows found in {Directory}", directory);

            return;
        }

        var files = Directory.GetFiles(directory, "*.json");

        var workflows = await dbContext
            .Workflows.Include(w => w.Levels.Where(w => w.IsNotDeleted))
            .ThenInclude(w => w.Stages.Where(s => s.IsNotDeleted))
            .ThenInclude(w => w.Privileges.Where(s => s.IsNotDeleted))
            .ToListAsync(cancellationToken: stoppingToken);

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, stoppingToken);

                var model = JsonSerializer.Deserialize<CreateWorkflowTaskModel>(
                    json,
                    options: jsonOptions
                );

                logger.LogInformation("Seeding workflow {WorkflowType}", model!.WorkflowType);
                var workflow = workflows.Find(w => w.WorkflowType == model!.WorkflowType);

                await CreateWorkflowAsync(dbContext, workflow, model!);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error when seeding.");
            }
        }

        await dbContext.SaveChangesAsync(stoppingToken);
    }

    private static async Task CreateWorkflowAsync(
        WorkflowDbContext dbContext,
        Core.Entities.Workflow? workflow,
        CreateWorkflowTaskModel model
    )
    {
        if (workflow == null)
        {
            workflow = new Core.Entities.Workflow
            {
                WorkflowType = model.WorkflowType,
                Name = model.Name
            };

            await dbContext.Workflows.AddAsync(workflow);
        }

        workflow.Name = model.Name;

        var existingLevels = workflow.Levels;

        foreach (var level in model.Levels)
        {
            var existingLevel = existingLevels.Find(l => l.Level == level.Level);

            if (existingLevel == null)
            {
                existingLevel = new WorkflowLevel
                {
                    Level = level.Level,
                    WorkflowId = workflow.WorkflowId,
                    Name = level.Name,
                    IsFinal = level.IsFinal,
                    WorkflowRole = level.WorkflowRole
                };

                await dbContext.WorkflowLevels.AddAsync(existingLevel);
            }

            existingLevel.Name = level.Name;
            existingLevel.IsFinal = level.IsFinal;
            existingLevel.WorkflowRole = level.WorkflowRole;

            var existingStages = existingLevel.Stages;

            foreach (var stage in level.Stages)
            {
                var existingStage = existingStages.Find(s => s.Stage == stage.Stage);

                if (existingStage == null)
                {
                    existingStage = new WorkflowLevelStage
                    {
                        Stage = stage.Stage,
                        WorkflowLevelId = existingLevel.WorkflowLevelId,
                        Name = stage.Name,
                        IsFinal = stage.IsFinal
                    };

                    await dbContext.WorkflowLevelStages.AddAsync(existingStage);
                }

                existingStage.Name = stage.Name;
                existingStage.StageIdentifier = stage.StageIdentifier;
                existingStage.IsFinal = stage.IsFinal;

                var existingPrivileges = existingStage.Privileges;

                foreach (var privilege in stage.Privileges)
                {
                    var existingPrivilege = existingPrivileges.Find(p =>
                        p.PrivilegeType == privilege.PrivilegeType
                    );

                    if (existingPrivilege == null)
                    {
                        existingPrivilege = new WorkflowLevelStagePrivilege
                        {
                            PrivilegeType = privilege.PrivilegeType,
                            PrivilegeRequirementType = privilege.PrivilegeRequirementType,
                            StageId = existingStage.WorkflowLevelStageId
                        };

                        await dbContext.WorkflowLevelStagePrivileges.AddAsync(existingPrivilege);
                    }

                    existingPrivilege.PrivilegeRequirementType = privilege.PrivilegeRequirementType;
                }

                var privilegesToRemove = existingPrivileges
                    .Where(p => !stage.Privileges.Exists(mp => mp.PrivilegeType == p.PrivilegeType))
                    .ToList();

                privilegesToRemove.ForEach(r => r.IsDeleted = true);
            }

            var stagesToRemove = existingStages
                .Where(s => !level.Stages.Exists(ms => ms.Stage == s.Stage))
                .ToList();

            stagesToRemove.ForEach(r => r.IsDeleted = true);
        }

        var levelsToRemove = existingLevels
            .Where(l => !model.Levels.Exists(ml => ml.Level == l.Level))
            .ToList();

        levelsToRemove.ForEach(r => r.IsDeleted = true);
    }
}
