using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

[AuditableTable]
[Table("WorkflowLevelStagePrivileges", Schema = Constants.Schema.Workflow)]
public class WorkflowLevelStagePrivilege : AuditableEntity
{
    [Key]
    public Guid WorkflowLevelStagePrivilegeId { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(Stage))]
    public required Guid StageId { get; set; }

    public WorkflowLevelStage Stage { get; set; } = null!;

    public required Enums.WorkflowPrivilegeType PrivilegeType { get; set; }
    public required Enums.WorkflowPrivilegeRequirementType PrivilegeRequirementType { get; set; }
}

public class WorkflowLevelPrivilegeConfiguration
    : AuditableEntityConfiguration<WorkflowLevelStagePrivilege>
{
    public override void Configure(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<WorkflowLevelStagePrivilege> builder
    )
    {
        base.Configure(builder);
        builder.HasOne(x => x.Stage).WithMany(x => x.Privileges).HasForeignKey(x => x.StageId);
    }
}
