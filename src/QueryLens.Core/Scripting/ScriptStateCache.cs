using Microsoft.CodeAnalysis.Scripting;
using System.Collections.Concurrent;

namespace QueryLens.Core.Scripting;

/// <summary>
/// Thread-safe cache of warm Roslyn <see cref="ScriptState{T}"/> instances,
/// keyed by assembly path + DbContext type name.
///
/// A warm state amortizes the cost of Roslyn compilation (~500ms cold start)
/// across subsequent calls. Each entry is invalidated when the assembly's
/// last-write timestamp changes (i.e., a rebuild happened).
/// </summary>
internal sealed class ScriptStateCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    /// <summary>
    /// Returns a cached entry if one exists and is still fresh; otherwise null.
    /// Callers that get null must build a new state and call <see cref="Store"/>.
    /// </summary>
    public ScriptState<object>? TryGet(string assemblyPath, string dbContextTypeName, DateTime assemblyTimestamp)
    {
        var key = MakeKey(assemblyPath, dbContextTypeName);
        if (_entries.TryGetValue(key, out var entry) && entry.AssemblyTimestamp == assemblyTimestamp)
            return entry.State;

        // Stale or absent — remove so the next Store() wins cleanly.
        _entries.TryRemove(key, out _);
        return null;
    }

    /// <summary>
    /// Stores a freshly-built <see cref="ScriptState{T}"/> in the cache.
    /// </summary>
    public void Store(string assemblyPath, string dbContextTypeName, DateTime assemblyTimestamp, ScriptState<object> state)
    {
        var key = MakeKey(assemblyPath, dbContextTypeName);
        _entries[key] = new CacheEntry(state, assemblyTimestamp);
    }

    /// <summary>Removes all cached entries for the given assembly path.</summary>
    public void Invalidate(string assemblyPath)
    {
        foreach (var key in _entries.Keys.Where(k => k.StartsWith(assemblyPath + "::", StringComparison.Ordinal)))
            _entries.TryRemove(key, out _);
    }

    private static string MakeKey(string assemblyPath, string dbContextTypeName) =>
        $"{assemblyPath}::{dbContextTypeName}";

    private sealed record CacheEntry(ScriptState<object> State, DateTime AssemblyTimestamp);
}
