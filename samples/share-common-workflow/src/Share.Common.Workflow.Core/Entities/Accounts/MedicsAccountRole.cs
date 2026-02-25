using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

[AuditableTable]
[Table("MedicsAccountRoles", Schema = Constants.Schema.Auth)]
public class MedicsAccountRole : AuditableEntity
{
    [Key]
    public Guid MedicsAccountRoleId { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(Account))]
    public required Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    [ForeignKey(nameof(MedicsRole))]
    public required Guid MedicsRoleId { get; set; }
    public MedicsRole MedicsRole { get; set; } = null!;
}
