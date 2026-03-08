// ReSharper disable once CheckNamespace
namespace QueryLens.Core;

public interface IQueryLensDbContextFactory<out TContext>
    where TContext : Microsoft.EntityFrameworkCore.DbContext
{
    TContext CreateOfflineContext();
}
