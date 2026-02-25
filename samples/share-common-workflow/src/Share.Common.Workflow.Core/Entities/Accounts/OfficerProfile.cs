using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share.Common.Workflow.Core.Domain;
using Share.Lib.Bootstrap.Api.Core.Entities;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

[AuditableTable]
[Table("OfficerProfiles", Schema = Constants.Schema.Auth)]
public class OfficerProfile : AuditableEntity
{
    [Key]
    public Guid OfficerProfileId { get; set; } = Guid.NewGuid();

    [StringLength(Constants.Entities.OfficerProfile.NameMaxLength)]
    public required string Name { get; set; }

    [ForeignKey(nameof(Account))]
    public required Guid AccountId { get; set; }

    public Account Account { get; set; } = null!;

    [StringLength(Constants.Entities.OfficerProfile.EmailMaxLength)]
    public required string Email { get; set; }

    public required Enums.AccountStatus Status { get; set; }

    public string? Designation { get; set; }

    public string? SoeId { get; set; }
    public DateTime? LastSyncTimestamp { get; set; }
    public Guid? LastTransactionId { get; set; }
}

public class OfficerProfileConfiguration : AuditableEntityConfiguration<OfficerProfile>
{
    public override void Configure(EntityTypeBuilder<OfficerProfile> builder)
    {
        base.Configure(builder);

        builder
            .HasOne(x => x.Account)
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
