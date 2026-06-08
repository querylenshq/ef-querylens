using EFQueryLens.Core.Scaffolding;

namespace EFQueryLens.Core.Tests.Scaffolding;

public sealed class ProviderDetectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ql-detect-{Guid.NewGuid():N}");

    public ProviderDetectorTests() => Directory.CreateDirectory(_dir);

    private string WriteCsproj(string packageId)
    {
        var path = Path.Combine(_dir, "App.csproj");
        File.WriteAllText(path,
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"{packageId}\" Version=\"9.0.0\" /></ItemGroup></Project>");
        return Path.Combine(_dir, "bin", "App.dll"); // non-existent dll → no deps.json
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", ProviderKind.SqlServer)]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", ProviderKind.Npgsql)]
    [InlineData("Pomelo.EntityFrameworkCore.MySql", ProviderKind.MySql)]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", ProviderKind.Sqlite)]
    public void Detect_FromCsprojPackageReference(string packageId, ProviderKind expected)
    {
        var assemblyPath = WriteCsproj(packageId);

        var result = ProviderDetector.Detect(assemblyPath, _dir);

        Assert.Equal(expected, result.Provider);
    }

    [Fact]
    public void Detect_DepsJson_TakesPrecedenceOverCsproj()
    {
        // csproj says SqlServer, deps.json (next to the dll) says Npgsql → deps wins.
        File.WriteAllText(Path.Combine(_dir, "App.csproj"),
            "<Project><ItemGroup><PackageReference Include=\"Microsoft.EntityFrameworkCore.SqlServer\" /></ItemGroup></Project>");
        var dll = Path.Combine(_dir, "App.dll");
        File.WriteAllText(dll, "");
        File.WriteAllText(Path.Combine(_dir, "App.deps.json"),
            "{ \"libraries\": { \"Npgsql.EntityFrameworkCore.PostgreSQL/9.0.4\": {} } }");

        var result = ProviderDetector.Detect(dll, _dir);

        Assert.Equal(ProviderKind.Npgsql, result.Provider);
        Assert.Equal("deps.json", result.Source);
    }

    [Fact]
    public void Detect_AmbiguousPackages_DisambiguatedBySourceScan()
    {
        File.WriteAllText(Path.Combine(_dir, "App.csproj"),
            "<Project><ItemGroup>" +
            "<PackageReference Include=\"Microsoft.EntityFrameworkCore.SqlServer\" />" +
            "<PackageReference Include=\"Npgsql.EntityFrameworkCore.PostgreSQL\" />" +
            "</ItemGroup></Project>");
        File.WriteAllText(Path.Combine(_dir, "Startup.cs"),
            "options.UseNpgsql(connectionString);");

        var result = ProviderDetector.Detect(Path.Combine(_dir, "bin", "App.dll"), _dir);

        Assert.Equal(ProviderKind.Npgsql, result.Provider);
        Assert.Equal("source", result.Source);
    }

    [Fact]
    public void Detect_NoSignals_ReturnsUnknown()
    {
        var result = ProviderDetector.Detect(Path.Combine(_dir, "bin", "App.dll"), _dir);

        Assert.Equal(ProviderKind.Unknown, result.Provider);
    }

    [Fact]
    public void Detect_ProjectablesReference_IsFlagged()
    {
        File.WriteAllText(Path.Combine(_dir, "App.csproj"),
            "<Project><ItemGroup>" +
            "<PackageReference Include=\"Microsoft.EntityFrameworkCore.SqlServer\" />" +
            "<PackageReference Include=\"EntityFrameworkCore.Projectables\" />" +
            "</ItemGroup></Project>");

        var result = ProviderDetector.Detect(Path.Combine(_dir, "bin", "App.dll"), _dir);

        Assert.Equal(ProviderKind.SqlServer, result.Provider);
        Assert.True(result.UseProjectables);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }
}
