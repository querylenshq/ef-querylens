using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace EFQueryLens.Lsp;

public class DocumentManager
{
    private readonly ConcurrentDictionary<string, string> _documents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CachedSyntaxTree> _syntaxTrees = new(StringComparer.Ordinal);

    private sealed record CachedSyntaxTree(string Text, SyntaxTree Tree);

    public void UpdateDocument(string documentUri, string text)
    {
        _documents[documentUri] = text;
        _syntaxTrees.TryRemove(documentUri, out _);
    }

    public void RemoveDocument(string documentUri)
    {
        _documents.TryRemove(documentUri, out _);
        _syntaxTrees.TryRemove(documentUri, out _);
    }

    public string? GetDocumentText(string documentUri)
    {
        return _documents.TryGetValue(documentUri, out var text) ? text : null;
    }

    public SyntaxTree GetOrParseSyntaxTree(string documentUri, string text)
    {
        if (_syntaxTrees.TryGetValue(documentUri, out var cached) && cached.Text == text)
        {
            return cached.Tree;
        }

        var tree = CSharpSyntaxTree.ParseText(text);
        _syntaxTrees[documentUri] = new CachedSyntaxTree(text, tree);
        return tree;
    }
}
