using System.Collections.Concurrent;
using EFQueryLens.Lsp.Parsing;

namespace EFQueryLens.Lsp.Handlers;

/// <summary>
/// Detects assembly rebuilds on <c>didSave</c> by comparing the compiled DLL fingerprint
/// (path + size + last-write) and invalidates hover + daemon caches when it changes.
/// </summary>
internal sealed class AssemblyChangeTracker
{
    private readonly ConcurrentDictionary<string, string> _lastFingerprints =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HoverHandler _hover;

    public AssemblyChangeTracker(HoverHandler hover) => _hover = hover;

    public void CheckOnSave(string filePath)
    {
        var fingerprint = AssemblyResolver.TryGetAssemblyFingerprint(filePath);
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return;
        }

        var assemblyPath = AssemblyResolver.TryGetTargetAssembly(filePath);
        if (string.IsNullOrWhiteSpace(assemblyPath)
            || assemblyPath.StartsWith("DEBUG_FAIL", StringComparison.Ordinal)
            || !File.Exists(assemblyPath))
        {
            return;
        }

        assemblyPath = Path.GetFullPath(assemblyPath);

        if (_lastFingerprints.TryGetValue(assemblyPath, out var previous)
            && string.Equals(previous, fingerprint, StringComparison.Ordinal))
        {
            return;
        }

        _lastFingerprints[assemblyPath] = fingerprint;

        if (previous is not null)
        {
            _hover.OnAssemblyChanged();
        }
    }
}
