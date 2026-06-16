using EFQueryLens.Core.Contracts;
using EFQueryLens.Core.Scaffolding;
using EFQueryLens.Lsp.Services;

namespace EFQueryLens.Lsp.Engine;

internal sealed class LazyQueryLensEngine : IQueryLensEngine, IEngineControl
{
    private readonly Func<CancellationToken, Task<IQueryLensEngine>> _factory;
    private readonly QueryLensStatusTracker _statusTracker;
    private readonly bool _debugEnabled;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IQueryLensEngine? _inner;
    private bool _disposed;

    public LazyQueryLensEngine(
        Func<CancellationToken, Task<IQueryLensEngine>> factory,
        QueryLensStatusTracker statusTracker,
        bool debugEnabled)
    {
        _factory = factory;
        _statusTracker = statusTracker;
        _debugEnabled = debugEnabled;
    }

    public async Task<QueryTranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken ct = default) =>
        await (await GetEngineAsync(ct)).TranslateAsync(request, ct);

    public async Task<ModelSnapshot> InspectModelAsync(
        ModelInspectionRequest request,
        CancellationToken ct = default) =>
        await (await GetEngineAsync(ct)).InspectModelAsync(request, ct);

    public async Task InvalidateAssemblyCachesAsync(CancellationToken ct = default) =>
        await (await GetEngineAsync(ct)).InvalidateAssemblyCachesAsync(ct);

    public async Task PingAsync(CancellationToken ct = default) =>
        await (await RequireControlAsync(ct, "Engine ping")).PingAsync(ct);

    public async Task RestartAsync(CancellationToken ct = default) =>
        await (await RequireControlAsync(ct, "Engine restart")).RestartAsync(ct);

    public async Task InvalidateCacheAsync(CancellationToken ct = default) =>
        await (await RequireControlAsync(ct, "Engine cache invalidation")).InvalidateCacheAsync(ct);

    public async Task WarmTranslateAsync(TranslationRequest request, CancellationToken ct = default) =>
        await (await RequireControlAsync(ct, "Engine warm translate")).WarmTranslateAsync(request, ct);

    public async Task<SetupResult> SetupAsync(SetupRequest request, CancellationToken ct = default) =>
        await (await RequireControlAsync(ct, "Setup")).SetupAsync(request, ct);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_inner is not null)
        {
            await _inner.DisposeAsync();
        }

        _gate.Dispose();
    }

    private async Task<IEngineControl> RequireControlAsync(CancellationToken ct, string operation)
    {
        var engine = await GetEngineAsync(ct);
        if (engine is IEngineControl control)
        {
            return control;
        }

        throw new InvalidOperationException($"{operation} is unavailable for this engine mode.");
    }

    private async Task<IQueryLensEngine> GetEngineAsync(CancellationToken ct)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LazyQueryLensEngine));
        }

        if (_inner is not null)
        {
            return _inner;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_inner is not null)
            {
                return _inner;
            }

            try
            {
                if (_debugEnabled)
                {
                    Console.Error.WriteLine("[QL-LSP] engine-lazy-start");
                }

                _inner = await _factory(ct);
                _statusTracker.SetDaemonReady(ready: true);
                return _inner;
            }
            catch (Exception ex)
            {
                var message = $"QueryLens engine failed to start: {ex.Message}";
                _statusTracker.SetDaemonUnavailable(message);
                Console.Error.WriteLine($"[QL-LSP] engine-lazy-start failed type={ex.GetType().Name} message={ex.Message}");
                throw new InvalidOperationException(message, ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
