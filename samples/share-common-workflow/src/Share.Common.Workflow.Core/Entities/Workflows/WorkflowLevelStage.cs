using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

[AuditableTable]
[Table("WorkflowLevelStages", Schema = Constants.Schema.Workflow)]
public class WorkflowLevelStage : AuditableEntity
{
    [Key]
    public Guid WorkflowLevelStageId { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(WorkflowLevel))]
    public required Guid WorkflowLevelId { get; set; }

    public required int Stage { get; set; }
    public required bool IsFinal { get; set; }

    [StringLength(Constants.Entities.WorkflowLevelStage.NameMaxLength)]
    public required string Name { get; set; }

    public Enums.WorkflowStageIdentifier StageIdentifier { get; set; } =
        Enums.WorkflowStageIdentifier.None;

    public WorkflowLevel WorkflowLevel { get; set; } = null!;
    public List<WorkflowLevelStagePrivilege> Privileges { get; set; } = [];
}

public class WorkflowLevelStageConfiguration : AuditableEntityConfiguration<WorkflowLevelStage>
{
    public override void Configure(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<WorkflowLevelStage> builder
    )
    {
        base.Configure(builder);
        builder
            .HasOne(x => x.WorkflowLevel)
            .WithMany(x => x.Stages)
            .HasForeignKey(x => x.WorkflowLevelId);
    }
}
