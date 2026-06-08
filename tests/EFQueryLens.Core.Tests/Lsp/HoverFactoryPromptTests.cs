using EFQueryLens.Lsp.Services;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class HoverFactoryPromptTests
{
    [Fact]
    public void BuildFactoryMissingMarkdown_WithoutSourcePath_ShowsSetupNeeded()
    {
        var markdown = HoverPreviewService.BuildFactoryMissingMarkdown(null);

        Assert.Contains("setup needed", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("efquerylens.setup", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("rebuild needed", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFactoryMissingMarkdown_WithMissingAssembly_ShowsSetupNeeded()
    {
        var markdown = HoverPreviewService.BuildFactoryMissingMarkdown(@"C:\does-not-exist\ReportService.cs");

        Assert.Contains("setup needed", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFactoryMissingMarkdown_ForRiderClient_UsesAltEnterHintWithoutSchemeLink()
    {
        var previousClient = Environment.GetEnvironmentVariable("QUERYLENS_CLIENT");
        try
        {
            Environment.SetEnvironmentVariable("QUERYLENS_CLIENT", "rider");
            var filePath = Path.Combine(Path.GetTempPath(), "QueryHandler.cs");
            var markdown = HoverPreviewService.BuildFactoryMissingMarkdown(filePath, line: 4, character: 12);

            Assert.Contains("Alt+Enter", markdown, StringComparison.Ordinal);
            Assert.Contains("Set up QueryLens", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("efquerylens://", markdown, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUERYLENS_CLIENT", previousClient);
        }
    }

    [Fact]
    public void BuildFactoryMissingMarkdown_ForVisualStudioClient_UsesEfQueryLensSetupLink()
    {
        var previousClient = Environment.GetEnvironmentVariable("QUERYLENS_CLIENT");
        try
        {
            Environment.SetEnvironmentVariable("QUERYLENS_CLIENT", "vs");
            var filePath = Path.Combine(Path.GetTempPath(), "QueryHandler.cs");
            var markdown = HoverPreviewService.BuildFactoryMissingMarkdown(filePath, line: 4, character: 12);

            Assert.Contains("efquerylens://setup", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("uri=", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("command:efquerylens.setup", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUERYLENS_CLIENT", previousClient);
        }
    }
}
