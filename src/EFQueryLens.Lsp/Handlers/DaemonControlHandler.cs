using EFQueryLens.Core.Contracts;
using EFQueryLens.Core.Scaffolding;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Engine;
using EFQueryLens.Lsp.Parsing;

namespace EFQueryLens.Lsp.Handlers;

internal sealed record DaemonRestartResponse(bool Success, string Message);
internal sealed record DaemonCacheInvalidateResponse(bool Success, string Message, int RemovedCachedResults, int RemovedInflightJobs);
internal sealed record SetupResponse(
    bool Success,
    string Message,
    string Action,
    string? GeneratedFilePath = null,
    ProviderKind Provider = ProviderKind.Unknown,
    bool RequiresReview = false);

internal sealed record SetupApplyRequest
{
    public string? HostProjectPath { get; init; }
    public ProviderKind ProviderOverride { get; init; } = ProviderKind.Unknown;
    public bool Force { get; init; }
}

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
        if (_engine is not IEngineControl control)
        {
            return new DaemonRestartResponse(false, "Engine restart is unavailable for this engine mode.");
        }

        try
        {
            await control.RestartAsync(cancellationToken);
            LogDebug("engine-restart-request success");
            return new DaemonRestartResponse(true, "Engine restarted.");
        }
        catch (Exception ex)
        {
            LogDebug($"engine-restart-request failed type={ex.GetType().Name} message={ex.Message}");
            return new DaemonRestartResponse(false, $"Engine restart failed: {ex.Message}");
        }
    }

    public async Task<DaemonCacheInvalidateResponse> InvalidateQueryCachesAsync(CancellationToken cancellationToken)
    {
        if (_engine is not IEngineControl control)
        {
            return new DaemonCacheInvalidateResponse(
                false,
                "Engine cache invalidation is unavailable for this engine mode.",
                0,
                0);
        }

        try
        {
            await control.InvalidateCacheAsync(cancellationToken);
            LogDebug("engine-cache-invalidate success");
            return new DaemonCacheInvalidateResponse(true, "Engine cache invalidated.", 0, 0);
        }
        catch (Exception ex)
        {
            LogDebug($"engine-cache-invalidate failed type={ex.GetType().Name} message={ex.Message}");
            return new DaemonCacheInvalidateResponse(false, $"Engine cache invalidation failed: {ex.Message}", 0, 0);
        }
    }

    public SetupDetectResult DetectSetupHosts(string filePath)
        => AssemblyResolver.DetectSetupHosts(filePath);

    /// <summary>
    /// Generates the offline DbContext factory for the executable project that owns
    /// <paramref name="filePath"/>. Resolves the built assembly + the executable project directory,
    /// then delegates to the daemon's <c>/setup</c> endpoint.
    /// </summary>
    public Task<SetupResponse> SetupAsync(string filePath, CancellationToken cancellationToken)
        => SetupApplyAsync(filePath, new SetupApplyRequest(), cancellationToken);

    public async Task<SetupResponse> SetupApplyAsync(
        string filePath,
        SetupApplyRequest applyRequest,
        CancellationToken cancellationToken)
    {
        if (_engine is not IEngineControl control)
        {
            return new SetupResponse(false, "Setup is unavailable for this engine mode.", "NotBuilt");
        }

        var host = AssemblyResolver.ResolveSetupHost(applyRequest.HostProjectPath, filePath);
        if (host is null)
        {
            return new SetupResponse(
                false,
                "Could not resolve an executable host project for setup.",
                "NotBuilt");
        }

        if (string.IsNullOrWhiteSpace(host.AssemblyPath)
            || host.AssemblyPath.StartsWith("DEBUG_FAIL", StringComparison.Ordinal)
            || !File.Exists(host.AssemblyPath))
        {
            return new SetupResponse(
                false,
                "Could not locate the compiled executable assembly. Build the project, then try Set up QueryLens again.",
                "NotBuilt");
        }

        if (string.IsNullOrWhiteSpace(host.ProjectDirectory))
        {
            return new SetupResponse(false, "Could not locate the executable project directory.", "NotBuilt");
        }

        try
        {
            var result = await control.SetupAsync(
                new SetupRequest
                {
                    AssemblyPath = host.AssemblyPath,
                    ProjectDirectory = host.ProjectDirectory,
                    ProviderOverride = applyRequest.ProviderOverride,
                    Force = applyRequest.Force,
                },
                cancellationToken);

            LogDebug($"setup action={result.Action} provider={result.Provider} contexts={result.Contexts.Count}");
            return new SetupResponse(
                result.Succeeded,
                result.Message,
                result.Action.ToString(),
                result.GeneratedFilePath,
                result.Provider,
                result.RequiresReview);
        }
        catch (Exception ex)
        {
            LogDebug($"setup failed type={ex.GetType().Name} message={ex.Message}");
            return new SetupResponse(false, $"Setup failed: {ex.Message}", "NotBuilt");
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
