using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

[AuditableTable]
[Table("MopProfileCompanies", Schema = Constants.Schema.Companies)]
public class MopProfileCompany : AuditableEntity
{
    [Key]
    public Guid MopProfileCompanyId { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(MopProfile))]
    public required Guid MopProfileId { get; set; }
    public MopProfile MopProfile { get; set; } = null!;

    [ForeignKey(nameof(Company))]
    public required Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
}

public class MopProfileCompanyConfiguration : AuditableEntityConfiguration<MopProfileCompany>
{
    public override void Configure(EntityTypeBuilder<MopProfileCompany> builder)
    {
        base.Configure(builder);

        builder
            .HasOne(x => x.MopProfile)
            .WithMany(x => x.Companies)
            .HasForeignKey(x => x.MopProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(x => x.Company)
            .WithMany(x => x.MopProfiles)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
