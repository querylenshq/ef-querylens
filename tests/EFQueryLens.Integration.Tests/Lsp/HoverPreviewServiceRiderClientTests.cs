using EFQueryLens.Integration.Tests.Lsp.Fakes;
using EFQueryLens.Lsp.Services;

namespace EFQueryLens.Integration.Tests.Lsp;

/// <summary>
/// Tests for Rider client detection in <see cref="HoverPreviewService"/> hover markdown.
/// Verifies that Rider clients receive Alt+Enter hints instead of clickable action links,
/// and non-Rider clients receive localhost HTTP action links.
/// </summary>
public class HoverPreviewServiceRiderClientTests : IDisposable
{
    private readonly string _originalClient;
    private readonly string _originalActionPort;

    public HoverPreviewServiceRiderClientTests()
    {
        _originalClient = Environment.GetEnvironmentVariable("QUERYLENS_CLIENT") ?? string.Empty;
        _originalActionPort = Environment.GetEnvironmentVariable("QUERYLENS_ACTION_PORT") ?? string.Empty;
    }

    public void Dispose()
    {
        SetEnv("QUERYLENS_CLIENT", _originalClient);
        SetEnv("QUERYLENS_ACTION_PORT", _originalActionPort);
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

    // ── Rider client: no action links, shows Alt+Enter hint ─────────────────

    [Fact]
    public void RiderClientMarkdown_ReceivesAltEnterHint()
    {
        SetEnv("QUERYLENS_CLIENT", "rider");
        SetEnv("QUERYLENS_ACTION_PORT", "9999");

        var engine = new FakeQueryLensEngine();
        var service = new HoverPreviewService(engine);

        // Call private Formatting method via reflection to test markdown generation.
        // This tests the path where Rider client skips action links entirely.
        var markdown = service.GetType()
            .GetMethod("FormatMarkdown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(
                service,
                [
                    "select u from Users u",
                    new List<string> { "SELECT * FROM Users u" },
                    0,
                    0,
                    null,
                    null,
                    new List<string>(),
                ]) as string;

        Assert.NotNull(markdown);
        Assert.Contains("Alt+Enter", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("http://127.0.0.1", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("efquerylens://", markdown, StringComparison.OrdinalIgnoreCase);
    }

    // ── Non-Rider client with action port: receives localhost links ────────

    [Fact]
    public void NonRiderClientMarkdown_ReceivesLocalhostActionLinks()
    {
        SetEnv("QUERYLENS_CLIENT", "vscode");
        SetEnv("QUERYLENS_ACTION_PORT", "9999");

        var engine = new FakeQueryLensEngine();
        var service = new HoverPreviewService(engine);

        var markdown = service.GetType()
            .GetMethod("FormatMarkdown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(
                service,
                [
                    "select u from Users u",
                    new List<string> { "SELECT * FROM Users u" },
                    5,
                    10,
                    "path/to/file.cs",
                    null,
                    new List<string>(),
                ]) as string;

        Assert.NotNull(markdown);
        Assert.Contains("http://127.0.0.1:9999", markdown, StringComparison.Ordinal);
        Assert.Contains("type=copysql", markdown, StringComparison.Ordinal);
        Assert.Contains("type=opensqleditor", markdown, StringComparison.Ordinal);
        Assert.Contains("type=recalculate", markdown, StringComparison.Ordinal);
    }

    // ── Default client (no QUERYLENS_CLIENT): uses action port if available ┐

    [Fact]
    public void DefaultClientWithActionPort_ReceivesLocalhostLinks()
    {
        SetEnv("QUERYLENS_CLIENT", "");
        SetEnv("QUERYLENS_ACTION_PORT", "8765");

        var engine = new FakeQueryLensEngine();
        var service = new HoverPreviewService(engine);

        var markdown = service.GetType()
            .GetMethod("FormatMarkdown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(
                service,
                [
                    "select x from X x",
                    new List<string> { "SELECT * FROM X" },
                    0,
                    0,
                    "test.cs",
                    null,
                    new List<string>(),
                ]) as string;

        Assert.NotNull(markdown);
        Assert.Contains("http://127.0.0.1:8765", markdown, StringComparison.Ordinal);
    }

    // ── No action port: no links emitted ─────────────────────────────────────

    [Fact]
    public void ClientWithoutActionPort_NoLinksEmitted()
    {
        SetEnv("QUERYLENS_CLIENT", "vscode");
        SetEnv("QUERYLENS_ACTION_PORT", "");

        var engine = new FakeQueryLensEngine();
        var service = new HoverPreviewService(engine);

        var markdown = service.GetType()
            .GetMethod("FormatMarkdown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(
                service,
                [
                    "select u from Users u",
                    new List<string> { "SELECT * FROM Users u" },
                    0,
                    0,
                    "test.cs",
                    null,
                    new List<string>(),
                ]) as string;

        Assert.NotNull(markdown);
        Assert.DoesNotContain("http://", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("efquerylens://", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[Copy SQL]", markdown, StringComparison.Ordinal);
    }

    // ── Action link encoding: line/character preserved ──────────────────────

    [Fact]
    public void ActionLinkEncoding_PreservesLineAndCharacter()
    {
        SetEnv("QUERYLENS_CLIENT", "vscode");
        SetEnv("QUERYLENS_ACTION_PORT", "7777");

        var engine = new FakeQueryLensEngine();
        var service = new HoverPreviewService(engine);

        const int testLine = 42;
        const int testChar = 15;

        var markdown = service.GetType()
            .GetMethod("FormatMarkdown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(
                service,
                [
                    "select u from Users u",
                    new List<string> { "SELECT * FROM Users u" },
                    testLine,
                    testChar,
                    "Users.cs",
                    null,
                    new List<string>(),
                ]) as string;

        Assert.NotNull(markdown);
        Assert.Contains($"line={testLine}", markdown, StringComparison.Ordinal);
        Assert.Contains($"character={testChar}", markdown, StringComparison.Ordinal);
    }
}
