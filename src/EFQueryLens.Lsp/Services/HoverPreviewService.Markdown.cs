using EFQueryLens.Core;
using EFQueryLens.Core.Contracts;

namespace EFQueryLens.Lsp.Services;

internal sealed partial class HoverPreviewService
{
    public async Task<HoverPreviewComputationResult> BuildMarkdownAsync(
        string filePath,
        string sourceText,
        int line,
        int character,
        CancellationToken cancellationToken)
    {
        Action<string> log = message => Console.Error.WriteLine($"[QL-Hover] {message}");
        var canonical = await BuildCanonicalAsync(
            filePath,
            sourceText,
            line,
            character,
            cancellationToken,
            log);

        if (canonical.Status is not QueryTranslationStatus.Ready && canonical.Success)
        {
            return new HoverPreviewComputationResult(
                Success: true,
                Output: BuildQueuedStatusMarkdown(canonical.Status, canonical.Message, canonical.AvgTranslationMs),
                Status: canonical.Status,
                AvgTranslationMs: canonical.AvgTranslationMs,
                LastTranslationMs: canonical.LastTranslationMs);
        }

        if (!canonical.Success)
        {
            return new HoverPreviewComputationResult(false, canonical.Message, canonical.Status);
        }

        var documentUri = DocumentPathResolver.ToUri(filePath);
        var markdown = BuildHoverMarkdown(
            canonical.Commands,
            canonical.Warnings,
            documentUri,
            line,
            character,
            canonical.Metadata,
            canonical.LastTranslationMs > 0 ? canonical.LastTranslationMs : canonical.AvgTranslationMs);
        Console.Error.WriteLine($"[QL-Hover] hover-markdown-ready line={line} char={character} markdownLen={markdown.Length}");
        return new HoverPreviewComputationResult(
            true,
            markdown,
            QueryTranslationStatus.Ready,
            canonical.AvgTranslationMs,
            canonical.LastTranslationMs);
    }

    private static string BuildQueuedStatusMarkdown(
        QueryTranslationStatus status,
        string statusText,
        double avgTranslationMs)
    {
        _ = status;
        _ = avgTranslationMs;
        var normalizedStatusText = string.IsNullOrWhiteSpace(statusText)
            ? "EF QueryLens - in queue"
            : statusText;
        return $"{normalizedStatusText}\n\n";
    }
}
