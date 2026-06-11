using System.Collections.Concurrent;
using EFQueryLens.Lsp.Parsing;

namespace EFQueryLens.Lsp.HoverPipeline;

/// <summary>
/// Per-document cache of <see cref="LinqChainInfo"/> results so repeated hovers do not
/// re-scan the full source on every position lookup. Invalidated on document change.
/// </summary>
internal sealed class DocumentLinqChainCache
{
    private readonly ConcurrentDictionary<string, CachedChains> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<LinqChainInfo> GetOrFindChains(string filePath, string sourceText)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        var hash = ComputeSourceHash(sourceText);
        var entry = _cache.GetOrAdd(normalizedPath, _ => new CachedChains());
        lock (entry)
        {
            if (entry.SourceHash == hash && entry.Chains is not null)
            {
                return entry.Chains;
            }

            entry.SourceHash = hash;
            entry.Chains = LspSyntaxHelper.FindAllLinqChains(sourceText).ToArray();
            return entry.Chains;
        }
    }

    public void Invalidate(string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        _cache.TryRemove(normalizedPath, out _);
    }

    public void Clear() => _cache.Clear();

    private static int ComputeSourceHash(string sourceText) => sourceText.GetHashCode(StringComparison.Ordinal);

    private sealed class CachedChains
    {
        public int SourceHash;
        public LinqChainInfo[]? Chains;
    }
}
