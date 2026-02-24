using Microsoft.EntityFrameworkCore;
using QueryLens.MySql;

namespace QueryLens.MySql.Tests;

/// <summary>
/// Unit tests for <see cref="MySqlProviderBootstrap"/>.
/// No database connection is required.
/// </summary>
public class MySqlProviderBootstrapTests
{
    [Fact]
    public void ProviderName_IsCorrect()
    {
        var bootstrap = new MySqlProviderBootstrap();
        Assert.Equal("Pomelo.EntityFrameworkCore.MySql", bootstrap.ProviderName);
    }

    [Fact]
    public void ConfigureOffline_DoesNotThrow()
    {
        var bootstrap = new MySqlProviderBootstrap();
        var builder   = new DbContextOptionsBuilder();
        var ex = Record.Exception(() => bootstrap.ConfigureOffline(builder));
        Assert.Null(ex);
    }

    [Fact]
    public void ConfigureOffline_BuilderHasOptions()
    {
        var bootstrap = new MySqlProviderBootstrap();
        var builder   = new DbContextOptionsBuilder();
        bootstrap.ConfigureOffline(builder);
        Assert.NotNull(builder.Options);
    }

    [Fact]
    public void ConfigureOffline_SetsProvider()
    {
        var bootstrap = new MySqlProviderBootstrap();
        var builder   = new DbContextOptionsBuilder();
        bootstrap.ConfigureOffline(builder);

        // Options should have extensions configured (at least the Pomelo one).
        Assert.NotEmpty(builder.Options.Extensions);
    }
}
