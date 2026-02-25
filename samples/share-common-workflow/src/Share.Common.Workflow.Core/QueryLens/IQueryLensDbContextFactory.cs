// ─────────────────────────────────────────────────────────────────────────────
// QueryLens interface stub
//
// This is a LOCAL copy of QueryLens.Core.IQueryLensDbContextFactory<T> placed
// in the QueryLens.Core namespace. No package reference to QueryLens.Core is
// needed — QueryLens discovers factories by full interface name via reflection
// and does not care which assembly the interface definition lives in.
//
// Keeping it here avoids cross-version conflicts (EF Core 8 ↔ 9) that would
// arise from adding a direct ProjectReference to QueryLens.Core.
// ─────────────────────────────────────────────────────────────────────────────
// ReSharper disable once CheckNamespace
namespace QueryLens.Core;

/// <summary>
/// Implement this to give QueryLens a fully-configured offline
/// <see cref="Microsoft.EntityFrameworkCore.DbContext"/> for SQL preview.
/// </summary>
public interface IQueryLensDbContextFactory<out TContext>
    where TContext : Microsoft.EntityFrameworkCore.DbContext
{
    /// <summary>Creates an offline context — no real DB connection needed.</summary>
    TContext CreateOfflineContext();
}
