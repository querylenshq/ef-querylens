using EFQueryLens.Core;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp.Parsing;

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
        Action<string> log = message => LogDebug($"markdown {message}");
        var canonical = await BuildCanonicalAsync(
            filePath,
            sourceText,
            line,
            character,
            cancellationToken,
            log);

        var result = FormatMarkdown(canonical, filePath, line, character);
        if (result.Success && result.Status is QueryTranslationStatus.Ready)
        {
            LogDebug($"markdown hover-markdown-ready line={line} char={character} markdownLen={result.Output.Length}");
        }

        return result;
    }

    private HoverPreviewComputationResult FormatMarkdown(
        HoverCanonicalComputationResult canonical,
        string? filePath = null,
        int line = 0,
        int character = 0)
    {
        if (canonical.Status is not QueryTranslationStatus.Ready && canonical.Success)
        {
            return new HoverPreviewComputationResult(
                Success: true,
                Output: BuildQueuedStatusMarkdown(canonical.Status, canonical.Message, canonical.AvgTranslationMs),
                Status: canonical.Status,
                AvgTranslationMs: canonical.AvgTranslationMs,
                LastTranslationMs: canonical.LastTranslationMs);
        }

        // A missing factory is an actionable, expected state — render it as a friendly prompt with
        // a one-click "Set up QueryLens" command link rather than a raw error block. Marked
        // Success so the caller renders the markdown as-is (not inside an error code fence) and the
        // command link stays clickable.
        if (!canonical.Success && IsNoFactoryError(canonical.Message))
        {
            return new HoverPreviewComputationResult(
                Success: true,
                Output: BuildFactoryMissingMarkdown(filePath, line, character),
                Status: QueryTranslationStatus.Ready);
        }

        if (!canonical.Success)
        {
            return new HoverPreviewComputationResult(false, canonical.Message, canonical.Status);
        }

        var markdown = BuildHoverMarkdown(
            canonical.Commands,
            canonical.Warnings,
            canonical.Metadata,
            canonical.LastTranslationMs > 0 ? canonical.LastTranslationMs : canonical.AvgTranslationMs,
            filePath,
            line,
            character);

        return new HoverPreviewComputationResult(
            true,
            markdown,
            QueryTranslationStatus.Ready,
            canonical.AvgTranslationMs,
            canonical.LastTranslationMs);
    }

    /// <summary>
    /// True when a translation failed only because no offline DbContext factory exists for the
    /// project — the case the one-click "Set up QueryLens" action resolves.
    /// </summary>
    internal static bool IsNoFactoryError(string? message)
        => !string.IsNullOrEmpty(message)
           && message.Contains("IQueryLensDbContextFactory", StringComparison.Ordinal);

    internal static string BuildFactoryMissingMarkdown(string? filePath, int line = 0, int character = 0)
        => !string.IsNullOrWhiteSpace(filePath)
           && AssemblyResolver.HostProjectHasQueryLensFactorySource(filePath)
            ? BuildRebuildNeededMarkdown(filePath, line, character)
            : BuildSetupNeededMarkdown(filePath, line, character);

    private static string BuildSetupNeededMarkdown(string? filePath, int line, int character)
    {
        if (IsRiderClient())
        {
            return "**EF QueryLens — setup needed**\n\n"
                   + "No offline DbContext factory was found for this project, so SQL can't be previewed yet.\n\n"
                   + "Use **Alt+Enter** → *EF QueryLens: Set up QueryLens*\n\n"
                   + "_Generates a git-ignored factory and prompts a rebuild — nothing is committed._";
        }

        var link = BuildSetupActionLink(filePath, line, character, "efquerylens.setup", "setup");
        return "**EF QueryLens — setup needed**\n\n"
               + "No offline DbContext factory was found for this project, so SQL can't be previewed yet.\n\n"
               + $"[⚙ Set up QueryLens for this project]({link})\n\n"
               + "_Generates a git-ignored factory and prompts a rebuild — nothing is committed._";
    }

    private static string BuildRebuildNeededMarkdown(string? filePath, int line, int character)
    {
        if (IsRiderClient())
        {
            return "**EF QueryLens — rebuild needed**\n\n"
                   + "An offline DbContext factory source file exists for this project, but it is not in the built assembly yet. "
                   + "Rebuild the executable host project, then hover again.\n\n"
                   + "Use **Alt+Enter** → *EF QueryLens: Reanalyze*\n\n"
                   + "_Factory lives under `Properties/QueryLens/` (git-ignored)._";
        }

        var link = BuildSetupActionLink(filePath, line, character, "efquerylens.recalculate", "recalculate");
        return "**EF QueryLens — rebuild needed**\n\n"
               + "An offline DbContext factory source file exists for this project, but it is not in the built assembly yet. "
               + "Rebuild the executable host project, then hover again.\n\n"
               + $"[↻ Recalculate preview]({link})\n\n"
               + "_Factory lives under `Properties/QueryLens/` (git-ignored)._";
    }

    private static bool IsRiderClient()
        => string.Equals(
            Environment.GetEnvironmentVariable("QUERYLENS_CLIENT"),
            "rider",
            StringComparison.OrdinalIgnoreCase);

    private static string BuildSetupActionLink(
        string? filePath,
        int line,
        int character,
        string vscodeCommand,
        string efQueryLensHost)
    {
        var client = Environment.GetEnvironmentVariable("QUERYLENS_CLIENT");
        if (string.Equals(client, "vscode", StringComparison.OrdinalIgnoreCase))
        {
            return $"command:{vscodeCommand}";
        }

        if (IsRiderClient())
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            try
            {
                var fileUri = Uri.EscapeDataString(new Uri(filePath).AbsoluteUri);
                return $"efquerylens://{efQueryLensHost}?uri={fileUri}&line={line}&character={character}";
            }
            catch
            {
                // Fall through to command link.
            }
        }

        return $"command:{vscodeCommand}";
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
