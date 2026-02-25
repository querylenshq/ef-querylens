using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

/// <summary>
/// This table is used to save workflow remarks, but not tied to Workflow Level Stage.
/// Instead it will be tied to Application.
/// </summary>
[AuditableTable]
[Table("AppWorkflowLevelStageActionRemarks", Schema = Constants.Schema.Application)]
public class AppWorkflowLevelStageActionRemark : AuditableEntity
{
    [Key]
    public Guid AppWorkflowLevelStageActionRemarkId { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(AppOfficer))]
    public required Guid AppOfficerId { get; set; }
    public ApplicationOfficer AppOfficer { get; set; } = null!;

    public required Guid ApplicationId { get; set; }

    /// <summary>
    /// Do not link it with AppWorkflowLevelStageAction table.
    /// This column is only for tracking purpose.
    /// </summary>
    public required Guid AppWorkflowLevelStageActionId { get; set; }

    public DateTime? CompletedAt { get; set; }

    [StringLength(Constants.Entities.AppWorkflowLevelStageAction.RemarksMaxLength)]
    public string? Remarks { get; set; }

    public DateTime? StartedAt { get; set; }
}

public class AppWorkflowLevelStageActionRemarkConfiguration
    : AuditableEntityConfiguration<AppWorkflowLevelStageActionRemark>
{
    public override void Configure(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AppWorkflowLevelStageActionRemark> builder
    )
    {
        base.Configure(builder);

        builder
            .HasOne(x => x.AppOfficer)
            .WithMany(r => r.StageActionRemarks)
            .HasForeignKey(x => x.AppOfficerId);
    }
}
