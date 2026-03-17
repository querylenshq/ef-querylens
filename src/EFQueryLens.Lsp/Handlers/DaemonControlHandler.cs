using EFQueryLens.Core;
using EFQueryLens.Core.Contracts;
using EFQueryLens.DaemonClient;
using EFQueryLens.Lsp;

namespace EFQueryLens.Lsp.Handlers;

internal sealed record DaemonRestartResponse(bool Success, string Message);
internal sealed record DaemonCacheInvalidateResponse(bool Success, string Message, int RemovedCachedResults, int RemovedInflightJobs);

internal sealed class DaemonControlHandler
{
    private readonly IQueryLensEngine _engine;
    private bool _debugEnabled;

    public DaemonControlHandler(IQueryLensEngine engine)
    {
        _engine = engine;
        _debugEnabled = LspEnvironment.ReadBool("QUERYLENS_DEBUG", fallback: false);
    }

    public async Task<DaemonRestartResponse> RestartAsync(CancellationToken cancellationToken)
    {
        if (_engine is not ResiliencyDaemonEngine resiliency)
        {
            return new DaemonRestartResponse(false, "Daemon restart is unavailable for this engine mode.");
        }

        try
        {
            var restarted = await resiliency.RestartDaemonAsync(cancellationToken);
            if (restarted)
            {
                LogDebug("daemon-restart-request success");
                return new DaemonRestartResponse(true, "Daemon restarted.");
            }

            return new DaemonRestartResponse(false, "Daemon restart did not complete.");
        }
        catch (Exception ex)
        {
            LogDebug($"daemon-restart-request failed type={ex.GetType().Name} message={ex.Message}");
            return new DaemonRestartResponse(false, $"Daemon restart failed: {ex.Message}");
        }
    }

    public async Task<DaemonCacheInvalidateResponse> InvalidateQueryCachesAsync(CancellationToken cancellationToken)
    {
        if (_engine is not ResiliencyDaemonEngine resiliency)
        {
            return new DaemonCacheInvalidateResponse(
                false,
                "Daemon cache invalidation is unavailable for this engine mode.",
                0,
                0);
        }

        try
        {
            var response = await resiliency.InvalidateQueryCachesAsync(cancellationToken);
            LogDebug(
                $"daemon-cache-invalidate success={response.Success} cachedRemoved={response.RemovedCachedResults} " +
                $"inflightRemoved={response.RemovedInflightJobs}");

            return new DaemonCacheInvalidateResponse(
                response.Success,
                string.IsNullOrWhiteSpace(response.Message)
                    ? (response.Success ? "Preview cache invalidated." : "Preview cache invalidation did not complete.")
                    : response.Message,
                response.RemovedCachedResults,
                response.RemovedInflightJobs);
        }
        catch (Exception ex)
        {
            LogDebug($"daemon-cache-invalidate failed type={ex.GetType().Name} message={ex.Message}");
            return new DaemonCacheInvalidateResponse(false, $"Preview cache invalidation failed: {ex.Message}", 0, 0);
        }
    }

    public void ApplyClientConfiguration(LspClientConfiguration configuration)
    {
        if (configuration.DebugEnabled.HasValue)
        {
            _debugEnabled = configuration.DebugEnabled.Value;
        }
    }

    private void LogDebug(string message)
    {
        if (!_debugEnabled)
        {
            return;
        }

        Console.Error.WriteLine($"[QL-DaemonCtl] {message}");
    }
}
