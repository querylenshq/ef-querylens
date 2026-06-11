using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Lsp.HoverPipeline;

internal sealed record HoverResult(
    QueryTranslationStatus Status,
    Hover? Markdown,
    QueryLensStructuredHoverResult? Structured,
    bool FromCache = false);
