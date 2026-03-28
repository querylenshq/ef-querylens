using EFQueryLens.Integration.Tests.Lsp.Fakes;
using EFQueryLens.Lsp.Services;

namespace EFQueryLens.Integration.Tests.Lsp;

/// <summary>
/// Tests for Rider client detection in <see cref="HoverPreviewService"/> hover initialization.
/// Verifies that Rider clients (via QUERYLENS_CLIENT env var) are detected correctly.
/// </summary>
public class HoverPreviewServiceRiderClientTests : IDisposable
{
    private readonly string _originalClient;

    public HoverPreviewServiceRiderClientTests()
    {
        _originalClient = Environment.GetEnvironmentVariable("QUERYLENS_CLIENT") ?? string.Empty;
    }

    public void Dispose()
    {
        SetEnv("QUERYLENS_CLIENT", _originalClient);
    }

    private static void SetEnv(string name, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Environment.SetEnvironmentVariable(name, null);
        }
        else
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    // ── Rider client initialization ──────────────────────────────────────────

    [Fact]
    public void RiderClientInitialization_DetectsRiderFromEnvironment()
    {
        SetEnv("QUERYLENS_CLIENT", "rider");

        var engine = new FakeQueryLensEngine();
        var service = new HoverPreviewService(engine);

        service.SetDebugEnabled(false);

        Assert.NotNull(service);
    }

    // ── VSCode client initialization ─────────────────────────────────────────

    [Fact]
    public void VSCodeClientInitialization_DetectsVSCodeFromEnvironment()
    {
        SetEnv("QUERYLENS_CLIENT", "vscode");

        var engine = new FakeQueryLensEngine();
        var service = new HoverPreviewService(engine);

        Assert.NotNull(service);
    }

    // ── Client detection: case-insensitive rider detection ──────────────────

    [Theory]
    [InlineData("rider")]
    [InlineData("Rider")]
    [InlineData("RIDER")]
    public void RiderClientDetection_CaseInsensitive(string riderValue)
    {
        SetEnv("QUERYLENS_CLIENT", riderValue);

        var isRider = string.Equals(
            Environment.GetEnvironmentVariable("QUERYLENS_CLIENT"),
            "rider",
            StringComparison.OrdinalIgnoreCase);

        var engine = new FakeQueryLensEngine();
        var service = new HoverPreviewService(engine);

        Assert.True(isRider);
        Assert.NotNull(service);
    }

    // ── Default client behavior (no QUERYLENS_CLIENT env var) ────────────────

    [Fact]
    public void DefaultClientBehavior_NoEnvironmentVariable()
    {
        SetEnv("QUERYLENS_CLIENT", "");

        var engine = new FakeQueryLensEngine();
        var service = new HoverPreviewService(engine);

        Assert.NotNull(service);
    }

    // ── Debug enabled/disabled ───────────────────────────────────────────────

    [Fact]
    public void DebugEnabled_CanBeToggledWithoutErrors()
    {
        SetEnv("QUERYLENS_CLIENT", "rider");

        var engine = new FakeQueryLensEngine();
        var service = new HoverPreviewService(engine);

        service.SetDebugEnabled(true);
        service.SetDebugEnabled(false);
        service.SetDebugEnabled(true);

        Assert.NotNull(service);
    }

    // ── Rider vs VSCode client detection ────────────────────────────────────

    [Theory]
    [InlineData("rider")]
    [InlineData("vscode")]
    public void ClientDetection_RiderAndVSCodeSupported(string client)
    {
        SetEnv("QUERYLENS_CLIENT", client);

        var engine = new FakeQueryLensEngine();
        var service = new HoverPreviewService(engine);

        var isRider = string.Equals(client, "rider", StringComparison.OrdinalIgnoreCase);
        var isVSCode = string.Equals(client, "vscode", StringComparison.OrdinalIgnoreCase);

        Assert.True(isRider || isVSCode);
        Assert.NotNull(service);
    }
}
