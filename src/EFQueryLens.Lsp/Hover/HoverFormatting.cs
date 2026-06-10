using EFQueryLens.Core;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Lsp.HoverPipeline;

internal static class HoverFormatting
{
    public static HoverResult FromCombined(CombinedHoverResult combined)
    {
        Hover? hover = null;
        if (combined.Markdown.Success
            || !combined.Markdown.Output.StartsWith("Could not extract a LINQ query expression", StringComparison.OrdinalIgnoreCase))
        {
            var markdownText = combined.Markdown.Success
                ? combined.Markdown.Output
                : $"**QueryLens Error**\n```text\n{combined.Markdown.Output}\n```";
            hover = CreateMarkdownHover(markdownText);
        }

        QueryLensStructuredHoverResult? structured = combined.Structured;
        if (structured is { Success: false }
            && structured.ErrorMessage?.StartsWith("Could not extract a LINQ query expression", StringComparison.OrdinalIgnoreCase) == true)
        {
            structured = null;
        }

        return new HoverResult(combined.Markdown.Status, hover, structured);
    }

    /// <summary>
    /// True when the hover represents a successful SQL translation worth caching across hovers.
    /// Error markdown and factory prompts are shown to the user but only successful SQL is durable.
    /// </summary>
    public static bool IsCacheableTranslation(HoverResult result)
        => result.Status is QueryTranslationStatus.Ready
           && result.Markdown is not null
           && result.Structured?.Success != false;

    public static HoverResult InQueuePlaceholder()
        => new(
            QueryTranslationStatus.InQueue,
            BuildInQueueHover(),
            BuildInQueueStructured());

    public static Hover BuildInQueueHover()
        => CreateMarkdownHover("**EF QueryLens** \u2014 translating query\u2026 hover again shortly.");

    public static QueryLensStructuredHoverResult BuildInQueueStructured()
        => new(
            Success: false,
            ErrorMessage: null,
            Statements: [],
            CommandCount: 0,
            SourceExpression: null,
            ExecutedExpression: null,
            DbContextType: null,
            ProviderName: null,
            SourceFile: null,
            SourceLine: 0,
            Warnings: [],
            EnrichedSql: null,
            Mode: null,
            Status: QueryTranslationStatus.InQueue,
            StatusMessage: "Translating query \u2014 hover again shortly.",
            AvgTranslationMs: 0);

    public static Hover CreateMarkdownHover(string markdown)
    {
        var content = new MarkupContent
        {
            Kind = MarkupKind.Markdown,
            Value = markdown,
        };

        return new Hover
        {
            Contents = new SumType<SumType<string, MarkedString>, SumType<string, MarkedString>[], MarkupContent>(content),
        };
    }
}
