using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

[AuditableTable]
[Table("AppWorkflowLevelStageActions", Schema = Constants.Schema.Application)]
public class AppWorkflowLevelStageAction : AuditableEntity
{
    [Key]
    public Guid AppWorkflowLevelStageActionId { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(AppWorkflowLevelStage))]
    public required Guid AppWorkflowLevelStageId { get; set; }

    public Enums.WorkflowDecision Decision { get; set; } = Enums.WorkflowDecision.Pending;

    [ForeignKey(nameof(AppOfficer))]
    public required Guid AppOfficerId { get; set; }

    public ApplicationOfficer AppOfficer { get; set; } = null!;
    public AppWorkflowLevelStage AppWorkflowLevelStage { get; set; } = null!;

    [StringLength(Constants.Entities.AppWorkflowLevelStageAction.RemarksMaxLength)]
    public string? Remarks { get; set; }

    public DateTime? DecisionMadeAt { get; set; }
    public DateTime? ApplicationCreatedAtValue { get; set; } // This is to solve overidden values when setting CreatedAt manually
}

public class AppWorkflowLevelStageActionConfiguration
    : AuditableEntityConfiguration<AppWorkflowLevelStageAction>
{
    public override void Configure(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AppWorkflowLevelStageAction> builder
    )
    {
        base.Configure(builder);
        builder
            .HasOne(x => x.AppWorkflowLevelStage)
            .WithMany(x => x.Actions)
            .HasForeignKey(x => x.AppWorkflowLevelStageId);

        builder
            .HasOne(x => x.AppOfficer)
            .WithMany(r => r.StageActions)
            .HasForeignKey(x => x.AppOfficerId);
    }
}
