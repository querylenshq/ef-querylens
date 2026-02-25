using Microsoft.EntityFrameworkCore;
using Share.Lib.EntityFrameworkCore;

namespace Share.Common.Workflow.Core.Entities;

[ImplementDbContext]
public partial interface IWorkflowEntities
{
    public DbSet<MedicsRole> MedicsRoles { get; set; }
}
