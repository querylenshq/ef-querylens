using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SampleApp;

/// <summary>
/// Design-time factory for <see cref="AppDbContext"/>.
///
/// EF Core tooling (<c>dotnet ef migrations</c>) and QueryLens both discover
/// this factory automatically. It uses a fake offline connection string —
/// no real database is ever contacted during SQL preview.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Pass a pre-created, closed MySqlConnection instead of a connection string.
        // Pomelo holds a reference to the object without calling Open().
        // ToQueryString() only needs the ServerVersion for SQL dialect - no connection needed.
        var connection = new MySqlConnector.MySqlConnection("Server=__offline__;Database=__querylens__");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(connection, new MySqlServerVersion(new Version(8, 0, 0)))
            .Options;
        return new AppDbContext(options);
    }
}
