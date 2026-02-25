using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.EntityFrameworkCore.Entities;

namespace Share.Common.Workflow.Core.Entities;

[Table("MedicsRoles", Schema = Constants.Schema.Auth)]
public class MedicsRole : BaseEntity
{
    [Key]
    public Guid MedicsRoleId { get; set; } = Guid.NewGuid();

    public required Enums.MedicsRoleType RoleType { get; set; }
    public Enums.WorkflowType? WorkflowType { get; set; }
}

public class MedicsRoleConfiguration : BaseEntityTypeConfiguration<MedicsRole>;
