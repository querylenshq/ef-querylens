using Microsoft.EntityFrameworkCore;
using Share.Lib.Bootstrap.Api.Core.Entities;

namespace Share.Common.Workflow.Core.Extensions;

public static class DbSetExtensions
{
    /// <summary>
    /// Set IsDeleted to true for all entities in the collection
    /// </summary
    public static void SetIsDeleted(
        this IEnumerable<AuditableEntity> entities,
        bool isDeleted = true
    )
    {
        foreach (var entity in entities)
        {
            entity.IsDeleted = isDeleted;
        }
    }
}
