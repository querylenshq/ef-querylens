using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

[AuditableTable]
[Table("AppWorkflowLevelStagePrivilegeActions", Schema = Constants.Schema.Application)]
public class AppWorkflowLevelStagePrivilegeAction : AuditableEntity
{
    [Key]
    public Guid AppWorkflowLevelStagePrivilegeActionId { get; set; } = Guid.NewGuid();

    public required Enums.WorkflowPrivilegeType ConditionType { get; set; }

    public DateTime? CompletedAt { get; set; }

    [StringLength(Constants.Entities.ApplicationWorkflowLevelStageCondition.RemarksMaxLength)]
    public string? Remarks { get; set; }

    [ForeignKey(nameof(ApplicationWorkflowLevelStage))]
    public required Guid ApplicationWorkflowLevelStageId { get; set; }

    public AppWorkflowLevelStage ApplicationWorkflowLevelStage { get; set; } = null!;
}

public class ApplicationWorkflowLevelStageConditionConfiguration
    : AuditableEntityConfiguration<AppWorkflowLevelStagePrivilegeAction>
{
    public override void Configure(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AppWorkflowLevelStagePrivilegeAction> builder
    )
    {
        base.Configure(builder);
        builder
            .HasOne(x => x.ApplicationWorkflowLevelStage)
            .WithMany(x => x.PrivilegeActions)
            .HasForeignKey(x => x.ApplicationWorkflowLevelStageId);
    }
}
