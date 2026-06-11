using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Lsp.Handlers;

internal sealed partial class HoverHandler
{
    public async Task<Hover?> HandleAsync(TextDocumentPositionParams request, CancellationToken cancellationToken)
    {
        var filePath = DocumentPathResolver.Resolve(request.TextDocument.Uri);
        _assemblyChangeTracker?.CheckAndInvalidateIfChanged(filePath);
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
            cancellationToken);

        if (result.Status is QueryTranslationStatus.Ready
            && result.Markdown is not null)
        {
            var assemblyPath = EFQueryLens.Lsp.Parsing.AssemblyResolver.TryGetTargetAssembly(filePath);
            if (!string.IsNullOrWhiteSpace(assemblyPath))
            {
                _statusTracker?.SetAssemblyWarmed(warmed: true, assemblyPath);
            }
        }

        return result.Markdown;
    }
}
