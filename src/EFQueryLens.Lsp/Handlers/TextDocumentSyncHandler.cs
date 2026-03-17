using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Lsp.Handlers;

internal sealed class TextDocumentSyncHandler
{
    private readonly DocumentManager _documentManager;

    public TextDocumentSyncHandler(DocumentManager documentManager)
    {
        _documentManager = documentManager;
    }

    public void DidOpen(DidOpenTextDocumentParams request)
    {
        _documentManager.UpdateDocument(
            request.TextDocument.Uri.ToString(),
            request.TextDocument.Text ?? string.Empty);
    }

    public void DidChange(DidChangeTextDocumentParams request)
    {
        var text = request.ContentChanges?.FirstOrDefault()?.Text;
        if (text is null)
        {
            return;
        }

        _documentManager.UpdateDocument(request.TextDocument.Uri.ToString(), text);
    }

    public void DidClose(DidCloseTextDocumentParams request)
    {
        _documentManager.RemoveDocument(request.TextDocument.Uri.ToString());
    }

    public void DidSave(DidSaveTextDocumentParams request)
    {
        if (request.Text is null)
        {
            return;
        }

        _documentManager.UpdateDocument(request.TextDocument.Uri.ToString(), request.Text);
    }
}
