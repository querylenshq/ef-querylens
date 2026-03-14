using EFQueryLens.Core;
using EFQueryLens.DaemonClient;

namespace EFQueryLens.Lsp.Handlers;

internal sealed record DaemonRestartResponse(bool Success, string Message);

internal sealed class DaemonControlHandler
{
    private readonly IQueryLensEngine _engine;
    private readonly bool _debugEnabled;

    public DaemonControlHandler(IQueryLensEngine engine)
    {
        _engine = engine;
        _debugEnabled = ReadBoolEnvironmentVariable("QUERYLENS_DEBUG", fallback: false);
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

    private static bool ReadBoolEnvironmentVariable(string variableName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
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
