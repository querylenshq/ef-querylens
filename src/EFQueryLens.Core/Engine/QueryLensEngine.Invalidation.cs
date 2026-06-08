namespace EFQueryLens.Core.Engine;

public sealed partial class QueryLensEngine
{
    public async Task InvalidateAssemblyCachesAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();

        var entries = _alcCache.Values.ToArray();
        _alcCache.Clear();

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            await ReleaseCachedContextAsync(entry, reason: "invalidate");
        }
    }
}
