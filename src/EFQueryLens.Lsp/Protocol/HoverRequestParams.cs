using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Lsp.Protocol;

/// <summary>LSP hover request parameters.</summary>
internal sealed class HoverRequestParams : TextDocumentPositionParams;
