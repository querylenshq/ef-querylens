using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Parsing;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Lsp.Handlers;

internal sealed partial class HoverHandler
{
    public async Task<QueryLensStructuredHoverResult?> HandleStructuredAsync(
        TextDocumentPositionParams request,
        CancellationToken cancellationToken
    )
    {
        var filePath = DocumentPathResolver.Resolve(request.TextDocument.Uri);
        var documentUri = request.TextDocument.Uri.ToString();
        var sourceText = await GetSourceTextAsync(documentUri, filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return null;
        }

        using var computeScope = _statusTracker?.BeginCompute();
        var result = await _coordinator.RequestAsync(
            filePath,
            sourceText,
            request.Position.Line,
            request.Position.Character,
            cancellationToken,
            nonBlocking: true
        );

        if (result.Status is QueryTranslationStatus.Ready && result.Structured is not null)
        {
            var assemblyPath = AssemblyResolver.TryGetTargetAssembly(filePath);
            if (!string.IsNullOrWhiteSpace(assemblyPath))
            {
                _statusTracker?.SetAssemblyWarmed(warmed: true, assemblyPath);
            }
        }

        return result.Structured;
    }
}
