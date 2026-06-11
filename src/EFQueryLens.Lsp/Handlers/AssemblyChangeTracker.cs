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

    /// <summary>
    /// Compares the compiled host DLL fingerprint and invalidates caches when it changes.
    /// Called on save and before hover/warmup so terminal rebuilds are picked up without a save.
    /// </summary>
    public void CheckAndInvalidateIfChanged(string filePath)
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

    public void CheckOnSave(string filePath) => CheckAndInvalidateIfChanged(filePath);
}
