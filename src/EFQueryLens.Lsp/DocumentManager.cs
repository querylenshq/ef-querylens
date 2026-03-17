using System.Collections.Concurrent;

namespace EFQueryLens.Lsp;

public class DocumentManager
{
    private readonly ConcurrentDictionary<string, string> _documents = new(StringComparer.Ordinal);

    public void UpdateDocument(string documentUri, string text)
    {
        _documents[documentUri] = text;
    }

    public void RemoveDocument(string documentUri)
    {
        _documents.TryRemove(documentUri, out _);
    }

    public string? GetDocumentText(string documentUri)
    {
        return _documents.TryGetValue(documentUri, out var text) ? text : null;
    }
}
