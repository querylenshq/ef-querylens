using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

[AuditableTable]
[Table("Workflows", Schema = Constants.Schema.Workflow)]
public class Workflow : AuditableEntity
{
    [Key]
    public Guid WorkflowId { get; set; } = Guid.NewGuid();

    [StringLength(Constants.Entities.Workflow.NameMaxLength)]
    public required string Name { get; set; }
    public required Enums.WorkflowType WorkflowType { get; set; }
    public List<WorkflowLevel> Levels { get; set; } = [];
    public List<AppWorkflow> Applications { get; set; } = [];
}

public class WorkflowConfiguration : AuditableEntityConfiguration<Workflow>;
