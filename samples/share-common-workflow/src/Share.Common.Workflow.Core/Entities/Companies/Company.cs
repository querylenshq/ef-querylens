using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

[AuditableTable]
[Table("Companies", Schema = Constants.Schema.Companies)]
public class Company : AuditableEntity
{
    [Key]
    public Guid CompanyId { get; set; } = Guid.NewGuid();

    [StringLength(Constants.Entities.Company.NameMaxLength)]
    public required string Name { get; set; } = default!;

    [StringLength(Constants.Entities.Company.UenNumberMaxLength)]
    public required string UenNumber { get; set; } = default!;

    public List<MopProfileCompany> MopProfiles { get; set; } = [];
}

public class CompanyConfiguration : AuditableEntityConfiguration<Company>
{
    public override void Configure(EntityTypeBuilder<Company> builder)
    {
        base.Configure(builder);

        builder.HasIndex(e => e.UenNumber).IsUnique();
    }
}
