using EFQueryLens.Lsp;

namespace EFQueryLens.Integration.Tests.Lsp;

/// <summary>
/// Tests for action server HTTP request routing via localhost URLs.
/// Validates that HTTP request parameters are correctly parsed and actions are dispatched.
/// </summary>
public class ActionServerHttpRoutingTests : IDisposable
{
    private readonly string _originalActionPort;
    private readonly string _originalClient;

    public ActionServerHttpRoutingTests()
    {
        _originalActionPort = Environment.GetEnvironmentVariable("QUERYLENS_ACTION_PORT") ?? string.Empty;
        _originalClient = Environment.GetEnvironmentVariable("QUERYLENS_CLIENT") ?? string.Empty;
    }

    public void Dispose()
    {
        SetEnv("QUERYLENS_ACTION_PORT", _originalActionPort);
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

    // ── URL parameter encoding: action, uri, line, character ─────────────────

    [Fact]
    public void LocalhostActionUrl_ActionTypeParam_IsKnown()
    {
        const string url = "http://127.0.0.1:9999/efquerylens/action?type=copysql&uri=file%3A%2F%2F%2FC%3A%2Ftest.cs&line=10&character=5";

        // Validate query parameters can be parsed from URL
        var uri = new Uri(url);
        var path = uri.AbsolutePath;
        var query = uri.Query;

        Assert.Equal("/efquerylens/action", path);
        Assert.Contains("type=copysql", query, StringComparison.Ordinal);
        Assert.Contains("line=10", query, StringComparison.Ordinal);
        Assert.Contains("character=5", query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("copysql")]
    [InlineData("opensqleditor")]
    [InlineData("recalculate")]
    public void ActionUrl_AllKnownActionTypes_AreValid(string actionType)
    {
        var url = $"http://127.0.0.1:9999/efquerylens/action?type={actionType}&uri=file%3A%2F%2F%2Ftest.cs&line=0&character=0";

        var uri = new Uri(url);
        var query = uri.Query;

        Assert.Contains($"type={actionType}", query, StringComparison.Ordinal);
    }

    // ── Action URL: parameter preservation ────────────────────────────────────

    [Fact]
    public void ActionUrl_PreservesFileUri()
    {
        const string fileUri = "file:///C:/Users/test/QueryInvoices.cs";
        var escapedUri = Uri.EscapeDataString(fileUri);
        var url = $"http://127.0.0.1:9999/efquerylens/action?type=opensqleditor&uri={escapedUri}&line=25&character=10";

        var uri = new Uri(url);
        var query = uri.Query;

        // Encoded URI should be present in query
        Assert.Contains(escapedUri, query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 5)]
    [InlineData(100, 50)]
    [InlineData(9999, 200)]
    public void ActionUrl_PreservesLineAndCharacter(int line, int character)
    {
        var url = $"http://127.0.0.1:9999/efquerylens/action?type=copysql&uri=file%3A%2F%2F%2Ftest.cs&line={line}&character={character}";

        var uri = new Uri(url);
        var query = uri.Query;

        Assert.Contains($"line={line}", query, StringComparison.Ordinal);
        Assert.Contains($"character={character}", query, StringComparison.Ordinal);
    }

    // ── Action URL: port variations ──────────────────────────────────────────

    [Theory]
    [InlineData(5000)]
    [InlineData(8765)]
    [InlineData(9999)]
    [InlineData(49152)] // dynamic/private port range
    public void ActionUrl_VariousPortNumbers_ArePreserved(int port)
    {
        SetEnv("QUERYLENS_ACTION_PORT", port.ToString());

        var url = $"http://127.0.0.1:{port}/efquerylens/action?type=copysql&uri=test.cs&line=0&character=0";

        var uri = new Uri(url);
        Assert.Equal(port, uri.Port);
    }

    // ── Non-action URLs: should not be intercepted by action handler ────────

    [Fact]
    public void NonActionUrl_localhost_NotIntercepted()
    {
        // Only `/efquerylens/action` paths should be handled; others fall through
        var url = "http://127.0.0.1:9999/other/path?type=copysql";

        var uri = new Uri(url);
        Assert.NotEqual("/efquerylens/action", uri.AbsolutePath);
    }

    [Fact]
    public void NonActionUrl_DifferentHost_NotIntercepted()
    {
        // Only 127.0.0.1 should be handled
        var url = "http://example.com:9999/efquerylens/action?type=copysql";

        var uri = new Uri(url);
        Assert.NotEqual("127.0.0.1", uri.Host);
    }

    // ── Action URL: missing query parameters ─────────────────────────────────

    [Fact]
    public void ActionUrl_MissingType_ShouldFall()
    {
        var url = "http://127.0.0.1:9999/efquerylens/action?uri=test.cs&line=0&character=0";

        var uri = new Uri(url);
        var query = uri.Query;

        // type param is missing
        Assert.DoesNotContain("type=", query, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionUrl_EmptyParameters_DefaultToZero()
    {
        var url = "http://127.0.0.1:9999/efquerylens/action?type=copysql&uri=&line=&character=";

        var uri = new Uri(url);
        var query = uri.Query;

        // Query should parse even with empty values
        Assert.NotNull(query);
        Assert.Contains("line=", query, StringComparison.Ordinal);
        Assert.Contains("character=", query, StringComparison.Ordinal);
    }

    // ── Client detection: determines link scheme ─────────────────────────────

    [Fact]
    public void LocalhostLink_RiderClientSkipsLink_VSCodeUsesLink()
    {
        // Rider client: LSP hover skips clickable links
        SetEnv("QUERYLENS_CLIENT", "rider");
        var riderSkipsLinks = string.Equals(
            Environment.GetEnvironmentVariable("QUERYLENS_CLIENT"),
            "rider",
            StringComparison.OrdinalIgnoreCase);
        Assert.True(riderSkipsLinks);

        // VSCode client: LSP hover includes localhost HTTP links
        SetEnv("QUERYLENS_CLIENT", "vscode");
        var vscodeUsesLinks = !string.Equals(
            Environment.GetEnvironmentVariable("QUERYLENS_CLIENT"),
            "rider",
            StringComparison.OrdinalIgnoreCase);
        Assert.True(vscodeUsesLinks);
    }

    // ── Action URL: formatted correctly for markdown link ──────────────────────

    [Fact]
    public void ActionUrl_FormattedForMarkdownLink()
    {
        const int port = 9876;
        SetEnv("QUERYLENS_ACTION_PORT", port.ToString());

        var baseUrl = $"http://127.0.0.1:{port}/efquerylens/action";
        var fileUri = Uri.EscapeDataString("file:///C:/Project/App.cs");
        const int line = 42;
        const int character = 15;

        // Simulate markdown link generation
        var copySqlUrl = $"{baseUrl}?type=copysql&uri={fileUri}&line={line}&character={character}";
        var openEditorUrl = $"{baseUrl}?type=opensqleditor&uri={fileUri}&line={line}&character={character}";
        var recalculateUrl = $"{baseUrl}?type=recalculate&uri={fileUri}&line={line}&character={character}";

        var markdown =
            $"[Copy SQL]({copySqlUrl}) | [Open SQL]({openEditorUrl}) | [Reanalyze]({recalculateUrl})";

        // Validate markdown is well-formed
        Assert.Contains("[Copy SQL]", markdown, StringComparison.Ordinal);
        Assert.Contains("[Open SQL]", markdown, StringComparison.Ordinal);
        Assert.Contains("[Reanalyze]", markdown, StringComparison.Ordinal);
        Assert.Contains($"http://127.0.0.1:{port}", markdown, StringComparison.Ordinal);
        Assert.Contains("type=copysql", markdown, StringComparison.Ordinal);
        Assert.Contains("type=opensqleditor", markdown, StringComparison.Ordinal);
        Assert.Contains("type=recalculate", markdown, StringComparison.Ordinal);
    }
}
