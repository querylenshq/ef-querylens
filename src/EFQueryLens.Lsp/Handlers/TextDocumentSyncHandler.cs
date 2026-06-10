using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Lsp.Handlers;

internal sealed class TextDocumentSyncHandler
{
    public DocumentManager DocumentManager { get; }
    private readonly TranslationPrewarmService? _prewarm;
    private readonly AssemblyChangeTracker? _assemblyChangeTracker;
    private readonly HoverHandler? _hoverHandler;

    public TextDocumentSyncHandler(
        DocumentManager documentManager,
        TranslationPrewarmService? prewarm = null,
        AssemblyChangeTracker? assemblyChangeTracker = null,
        HoverHandler? hoverHandler = null)
    {
        DocumentManager = documentManager;
        _prewarm = prewarm;
        _assemblyChangeTracker = assemblyChangeTracker;
        _hoverHandler = hoverHandler;
    }

    public void DidOpen(DidOpenTextDocumentParams request)
    {
        var text = request.TextDocument.Text ?? string.Empty;
        var uriString = request.TextDocument.Uri.ToString();
        DocumentManager.UpdateDocument(uriString, text);
        _prewarm?.WarmDocument(UriToFilePath(uriString), text);
    }

    public void DidChange(DidChangeTextDocumentParams request)
    {
        var text = request.ContentChanges?.FirstOrDefault()?.Text;
        if (text is null)
        {
            return;
        }

        var uriString = request.TextDocument.Uri.ToString();
        DocumentManager.UpdateDocument(uriString, text);
        var filePath = UriToFilePath(uriString);
        _hoverHandler?.OnDocumentChanged(filePath);
        _prewarm?.DebounceWarmDocument(filePath, text);
    }

    public void DidClose(DidCloseTextDocumentParams request)
    {
        DocumentManager.RemoveDocument(request.TextDocument.Uri.ToString());
    }

    public void DidSave(DidSaveTextDocumentParams request)
    {
        if (request.Text is null)
        {
            return;
        }

        var uriString = request.TextDocument.Uri.ToString();
        var filePath = UriToFilePath(uriString);
        DocumentManager.UpdateDocument(uriString, request.Text);
        _assemblyChangeTracker?.CheckOnSave(filePath);
        _prewarm?.WarmDocument(filePath, request.Text);
    }

    private static string UriToFilePath(string uriString)
    {
        try
        {
            return Uri.TryCreate(uriString, UriKind.Absolute, out var uri) ? uri.LocalPath : uriString;
        }
        catch
        {
            return uriString;
        }
    }
}
