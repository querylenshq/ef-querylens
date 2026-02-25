using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

[AuditableTable]
[Table("ApplicationOfficers", Schema = Constants.Schema.Application)]
public class ApplicationOfficer : AuditableEntity
{
    [Key]
    public Guid ApplicationOfficerId { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(AppWorkflow))]
    public required Guid AppWorkflowId { get; set; }

    public AppWorkflow AppWorkflow { get; set; } = null!;

    [ForeignKey(nameof(Officer))]
    public required Guid OfficerId { get; set; }

    public Account Officer { get; set; } = null!;
    public List<AppWorkflowLevelStageAction> StageActions { get; set; } = [];
    public List<AppWorkflowLevelStageActionRemark> StageActionRemarks { get; set; } = [];
}

public class ApplicationOfficerConfiguration : AuditableEntityConfiguration<ApplicationOfficer>
{
    public override void Configure(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<ApplicationOfficer> builder
    )
    {
        base.Configure(builder);
        builder.HasOne(x => x.Officer).WithMany().HasForeignKey(x => x.OfficerId);
    }
}
