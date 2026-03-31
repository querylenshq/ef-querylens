using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace EFQueryLens.Core.Scripting.Evaluation;

internal sealed class EvalRunnerCache
{
    private const int MaxEntries = 256;

    internal sealed record Entry(
        Assembly EvalAssembly,
        QueryEvaluator.SyncRunnerInvoker? SyncInvoker,
        QueryEvaluator.AsyncRunnerInvoker? AsyncInvoker,
        long LastAccessTicks,
        string? ExecutedExpression = null);

    private readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.Ordinal);

    internal bool TryGet(string key, [NotNullWhen(true)] out Entry? entry) => _cache.TryGetValue(key, out entry);

    internal void Store(string key, Entry entry)
    {
        _cache[key] = entry;
        QueryEvaluator.TrimCacheByLastAccess(_cache, MaxEntries, static e => e.LastAccessTicks);
    }

    internal void Touch(string key, Entry entry)
    {
        _cache.TryUpdate(
            key,
            entry with { LastAccessTicks = QueryEvaluator.GetUtcNowTicks() },
            entry);
    }

    internal void Invalidate(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return;

        var normalized = Path.GetFullPath(assemblyPath);
        var prefix = normalized + "|";
        foreach (var key in _cache.Keys
                     .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                     .ToList())
        {
            _cache.TryRemove(key, out _);
        }
    }
}
