using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

[AuditableTable]
[Table("AppWorkflowLevels", Schema = Constants.Schema.Application)]
public class AppWorkflowLevel : AuditableEntity
{
    [Key]
    public Guid AppWorkflowLevelId { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(AppWorkflow))]
    public required Guid AppWorkflowId { get; set; }

    [ForeignKey(nameof(WorkflowLevel))]
    public required Guid WorkflowLevelId { get; set; }
    public bool IsActive { get; set; }

    public AppWorkflow AppWorkflow { get; set; } = null!;
    public WorkflowLevel WorkflowLevel { get; set; } = null!;
    public List<AppWorkflowLevelStage> Stages { get; set; } = [];
}

public class ApplicationWorkflowLevelConfiguration : AuditableEntityConfiguration<AppWorkflowLevel>
{
    public override void Configure(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AppWorkflowLevel> builder
    )
    {
        base.Configure(builder);
        builder
            .HasOne(x => x.AppWorkflow)
            .WithMany(x => x.Levels)
            .HasForeignKey(x => x.AppWorkflowId);

        builder
            .HasOne(x => x.WorkflowLevel)
            .WithMany(x => x.Applications)
            .HasForeignKey(x => x.WorkflowLevelId);
    }
}
