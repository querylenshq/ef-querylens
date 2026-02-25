using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

[AuditableTable]
[Table("AppWorkflows", Schema = Constants.Schema.Application)]
public class AppWorkflow : AuditableEntity
{
    [Key]
    public Guid AppWorkflowId { get; set; } = Guid.NewGuid();

    public required Guid ApplicationId { get; set; }

    [ForeignKey(nameof(Workflow))]
    public required Guid WorkflowId { get; set; }

    public Workflow Workflow { get; set; } = null!;

    [ForeignKey(nameof(ParentStage))]
    public Guid? ParentStageId { get; set; }
    public AppWorkflowLevelStage? ParentStage { get; set; }

    public List<AppWorkflowLevel> Levels { get; set; } = [];
    public Enums.WorkflowStatus Status { get; set; } = Enums.WorkflowStatus.InProgress;

    public DateTime? CompletedAt { get; set; }
}

public class ApplicationWorkflowConfiguration : AuditableEntityConfiguration<AppWorkflow>
{
    public override void Configure(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AppWorkflow> builder
    )
    {
        base.Configure(builder);

        builder
            .HasOne(x => x.Workflow)
            .WithMany(x => x.Applications)
            .HasForeignKey(x => x.WorkflowId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(x => x.ParentStage)
            .WithMany(x => x.Workflows)
            .HasForeignKey(x => x.ParentStageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
