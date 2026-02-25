using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

[AuditableTable]
[Table("AppWorkflowLevelStages", Schema = Constants.Schema.Application)]
public class AppWorkflowLevelStage : AuditableEntity
{
    [Key]
    public Guid AppWorkflowLevelStageId { get; set; } = Guid.NewGuid();

    public required bool IsActive { get; set; }

    [ForeignKey(nameof(AppWorkflowLevel))]
    public required Guid AppWorkflowLevelId { get; set; }

    public AppWorkflowLevel AppWorkflowLevel { get; set; } = null!;

    [ForeignKey(nameof(Stage))]
    public required Guid StageId { get; set; }
    public WorkflowLevelStage Stage { get; set; } = null!;
    public List<AppWorkflow> Workflows { get; set; } = [];
    public List<AppWorkflowLevelStagePrivilegeAction> PrivilegeActions { get; set; } = [];
    public List<AppWorkflowLevelStageAction> Actions { get; set; } = [];
}

public class AppWorkflowLevelStageConfiguration
    : AuditableEntityConfiguration<AppWorkflowLevelStage>
{
    public override void Configure(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AppWorkflowLevelStage> builder
    )
    {
        base.Configure(builder);
        builder
            .HasOne(x => x.AppWorkflowLevel)
            .WithMany(x => x.Stages)
            .HasForeignKey(x => x.AppWorkflowLevelId);
    }
}
