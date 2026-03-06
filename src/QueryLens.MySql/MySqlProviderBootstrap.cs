using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using QueryLens.Core;

namespace QueryLens.MySql;

/// <summary>
/// Configures DbContextOptions for offline SQL generation using the Pomelo MySQL provider.
/// No real database connection is created — ToQueryString() works without one.
/// </summary>
public sealed class MySqlProviderBootstrap : IProviderBootstrap
{
    public string ProviderName => "Pomelo.EntityFrameworkCore.MySql";

    public void ConfigureOffline(DbContextOptionsBuilder builder)
    {
        // Pomelo requires a ServerVersion hint even for offline usage.
        // Default to MySQL 8.0 — the minimum version this tool targets.
        var serverVersion = new MySqlServerVersion(new Version(8, 0, 0));

        // Pass a pre-created, CLOSED MySqlConnection instead of a connection string.
        // Pomelo's UseMySql(DbConnection, ...) overload registers the provided connection
        // object directly — no connection lifecycle is triggered.
        // ToQueryString() only needs the ServerVersion for SQL dialect generation;
        // it never actually opens the connection.
        var connection = new MySqlConnection("Server=__offline__;Database=__querylens__");
        builder.UseMySql(connection, serverVersion);
    }
}
