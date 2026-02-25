using EntityFrameworkCore.Projectables;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using QueryLens.Core;
using Share.Common.Workflow.Core.Infrastructure.Services;

namespace Share.Common.Workflow.Core.QueryLens;

/// <summary>
/// Configures an offline <see cref="WorkflowDbContext"/> for QueryLens SQL preview.
///
/// Mirrors the options set by <c>AddShareMySqlDbContext</c> that affect SQL output:
/// <list type="bullet">
///   <item><description>
///     <c>SchemaBehavior(Translate)</c> — translates <c>schema.table</c> names to
///     <c>schema_table</c> (required for correct Pomelo table name generation).
///   </description></item>
///   <item><description>
///     <c>UseQuerySplittingBehavior(SplitQuery)</c> — matches production split-query behaviour.
///   </description></item>
///   <item><description>
///     <c>UseProjectables()</c> — enables <c>[Projectable]</c> property translation
///     (e.g. <c>IsNotDeleted</c>, <c>IsActive</c>).
///   </description></item>
/// </list>
/// </summary>
public class WorkflowQueryLensFactory : IQueryLensDbContextFactory<WorkflowDbContext>
{
    public WorkflowDbContext CreateOfflineContext() =>
        new(new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseMySql(
                "Server=localhost;Database=__querylens_offline__",
                new MySqlServerVersion(new Version(8, 0, 45)),
                o => o
                    .SchemaBehavior(
                        MySqlSchemaBehavior.Translate,
                        (schema, table) => $"{schema}_{table}")
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseProjectables()
            .Options);
}
