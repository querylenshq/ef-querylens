using Microsoft.EntityFrameworkCore;

namespace QueryLens.Core;

/// <summary>
/// Configures a <see cref="DbContextOptionsBuilder"/> with a fake/offline
/// connection string so that <c>ToQueryString()</c> works without a real
/// database connection.
///
/// The builder is created by <c>QueryEvaluator</c> using the user's ALC-side
/// EF Core type, so the resulting <c>DbContextOptions</c> are compatible with
/// the user's DbContext constructor.
///
/// Each provider package (MySql, Postgres, SqlServer) supplies one implementation.
/// </summary>
public interface IProviderBootstrap
{
    string ProviderName { get; }

    /// <summary>
    /// Configures <paramref name="builder"/> for offline SQL generation.
    /// Implementations call e.g. <c>builder.UseMySql(...)</c>.
    /// </summary>
    void ConfigureOffline(DbContextOptionsBuilder builder);
}

