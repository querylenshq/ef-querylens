using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

[AuditableTable]
[Table("WorkflowLevels", Schema = Constants.Schema.Workflow)]
public class WorkflowLevel : AuditableEntity
{
    [Key]
    public Guid WorkflowLevelId { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(Workflow))]
    public required Guid WorkflowId { get; set; }

    [StringLength(Constants.Entities.WorkflowLevel.NameMaxLength)]
    public required string Name { get; set; }

    public required int Level { get; set; }

    public required bool IsFinal { get; set; }

    public required Enums.WorkflowRole WorkflowRole { get; set; }
    public Workflow Workflow { get; set; } = null!;
    public List<WorkflowLevelStage> Stages { get; set; } = [];

    public List<AppWorkflowLevel> Applications { get; set; } = [];
}

public class WorkflowLevelConfiguration : AuditableEntityConfiguration<WorkflowLevel>
{
    public override void Configure(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<WorkflowLevel> builder
    )
    {
        base.Configure(builder);
        builder.HasOne(x => x.Workflow).WithMany(x => x.Levels).HasForeignKey(x => x.WorkflowId);
    }
}
