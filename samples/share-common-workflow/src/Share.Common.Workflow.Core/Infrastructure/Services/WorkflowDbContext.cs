using Audit.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Share.Common.Workflow.Core.Entities;
using Share.Common.Workflow.Core.Infrastructure.Interfaces;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.Bootstrap.Api.Core.Infrastructure.Interfaces;

namespace Share.Common.Workflow.Core.Infrastructure.Services;

public class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options)
    : AuditDbContext(options),
        IWorkflowDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IShareAuthDbContext).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IWorkflowDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    public DbSet<Entities.Workflow> Workflows { get; set; }
    public DbSet<ApplicationOfficer> ApplicationOfficers { get; set; }
    public DbSet<WorkflowLevel> WorkflowLevels { get; set; }
    public DbSet<WorkflowLevelStage> WorkflowLevelStages { get; set; }
    public DbSet<WorkflowLevelStagePrivilege> WorkflowLevelStagePrivileges { get; set; }
    public DbSet<AppWorkflowLevelStageAction> AppWorkflowLevelStageActions { get; set; }
    public DbSet<AppWorkflowLevelStageActionRemark> AppWorkflowLevelStageActionRemarks { get; set; }
    public DbSet<AppWorkflow> AppWorkflows { get; set; }
    public DbSet<AppWorkflowLevel> AppWorkflowLevels { get; set; }
    public DbSet<AppWorkflowLevelStagePrivilegeAction> AppWorkflowLevelStagePrivilegeActions { get; set; }
    public DbSet<AppWorkflowLevelStage> AppWorkflowLevelStages { get; set; }
    public DbSet<MedicsRole> MedicsRoles { get; set; }
    public DbSet<MopProfile> MopProfiles { get; set; }
    public DbSet<MopProfileCompany> MopProfileCompanies { get; set; }
    public DbSet<OfficerProfile> OfficerProfiles { get; set; }
    public DbSet<Company> Companies { get; set; }
    public DbSet<MedicsAccountRole> MedicsAccountRoles { get; set; }
    public DbSet<AuditWorkflow> AuditWorkflows { get; set; }
    public DbSet<AuditApplicationOfficer> AuditApplicationOfficers { get; set; }
    public DbSet<AuditWorkflowLevel> AuditWorkflowLevels { get; set; }
    public DbSet<AuditWorkflowLevelStage> AuditWorkflowLevelStages { get; set; }
    public DbSet<AuditWorkflowLevelStagePrivilege> AuditWorkflowLevelStagePrivileges { get; set; }
    public DbSet<AuditAppWorkflowLevelStageAction> AuditAppWorkflowLevelStageActions { get; set; }
    public DbSet<AuditAppWorkflowLevelStageActionRemark> AuditAppWorkflowLevelStageActionRemarks { get; set; }
    public DbSet<AuditAppWorkflow> AuditAppWorkflows { get; set; }
    public DbSet<AuditAppWorkflowLevel> AuditAppWorkflowLevels { get; set; }
    public DbSet<AuditAppWorkflowLevelStagePrivilegeAction> AuditAppWorkflowLevelStagePrivilegeActions { get; set; }
    public DbSet<AuditAppWorkflowLevelStage> AuditAppWorkflowLevelStages { get; set; }
    public DbSet<Account> Accounts { get; set; }
    public DbSet<AuditAccount> AuditAccounts { get; set; }
    public DbSet<AuditMopProfile> AuditMopProfiles { get; set; }
    public DbSet<AuditMopProfileCompany> AuditMopProfileCompanies { get; set; }
    public DbSet<AuditOfficerProfile> AuditOfficerProfiles { get; set; }
    public DbSet<AuditCompany> AuditCompanies { get; set; }
    public DbSet<AuditMedicsAccountRole> AuditMedicsAccountRoles { get; set; }
}
