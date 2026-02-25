using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

[AuditableTable]
[Table("MopProfiles", Schema = Constants.Schema.Auth)]
public class MopProfile : AuditableEntity
{
    [Key]
    public Guid MopProfileId { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(Account))]
    public required Guid AccountId { get; set; }

    public Account Account { get; set; } = null!;

    [StringLength(Constants.Entities.MopProfile.NameMaxLength)]
    public required string Name { get; set; }

    [StringLength(Constants.Entities.MopProfile.EmailMaxLength)]
    public required string Email { get; set; }
    public List<MopProfileCompany> Companies { get; set; } = [];
    public DateTime? LastSyncTimestamp { get; set; }
    public Guid? LastTransactionId { get; set; }
}

public class MopProfileConfiguration : AuditableEntityConfiguration<MopProfile>
{
    public override void Configure(EntityTypeBuilder<MopProfile> builder)
    {
        base.Configure(builder);

        builder
            .HasOne(x => x.Account)
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
