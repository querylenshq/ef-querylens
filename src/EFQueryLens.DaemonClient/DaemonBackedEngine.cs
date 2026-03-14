using System.Text.Json;
using System.Diagnostics;
using EFQueryLens.Core;
using EFQueryLens.Core.Daemon;
using StreamJsonRpc;

namespace EFQueryLens.DaemonClient;

/// <summary>
/// <see cref="IQueryLensEngine"/> backed by a QueryLens daemon over a named pipe.
/// The pipe connection is owned by this instance and disposed when the engine is disposed.
/// </summary>
public sealed class DaemonBackedEngine : IQueryLensEngine, IAsyncDisposable
{
    private readonly JsonRpc _rpc;
    private readonly IDaemonService _proxy;
    private readonly string _contextName;
    private readonly bool _debugEnabled;

    /// <summary>
    /// Creates a new <see cref="DaemonBackedEngine"/> wrapping an already-connected
    /// full-duplex <paramref name="pipeStream"/>. Takes ownership of the stream.
    /// </summary>
    public DaemonBackedEngine(Stream pipeStream, string contextName = "default")
    {
        _contextName = contextName;
        _debugEnabled = ReadBoolEnvironmentVariable("QUERYLENS_DEBUG", fallback: false);
        _rpc = new JsonRpc(BuildMessageHandler(pipeStream));
        _proxy = _rpc.Attach<IDaemonService>();
        _rpc.StartListening();
    }

    public async Task<QueryTranslationResult> TranslateAsync(
        TranslationRequest request, CancellationToken ct = default)
    {
        var payload = new DaemonTranslateRequest { ContextName = _contextName, Request = request };
        var sw = Stopwatch.StartNew();
        LogDebug(
            $"translate-rpc-start context={_contextName} assembly={request.AssemblyPath} " +
            $"exprLen={request.Expression?.Length ?? 0}");

        try
        {
            var response = await _proxy.TranslateAsync(payload, ct);
            sw.Stop();
            LogDebug(
                $"translate-rpc-finished context={_contextName} success={response.Result.Success} " +
                $"elapsedMs={sw.ElapsedMilliseconds} commands={response.Result.Commands.Count} " +
                $"sqlLen={(response.Result.Sql?.Length ?? 0)}");
            return response.Result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogDebug(
                $"translate-rpc-failed context={_contextName} elapsedMs={sw.ElapsedMilliseconds} " +
                $"type={ex.GetType().Name} message={ex.Message}");
            throw;
        }
    }

    public Task<ExplainResult> ExplainAsync(
        ExplainRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException("ExplainAsync is not yet exposed by the daemon protocol.");

    public async Task<ModelSnapshot> InspectModelAsync(
        ModelInspectionRequest request, CancellationToken ct = default)
    {
        var payload = new DaemonInspectRequest { ContextName = _contextName, Request = request };
        var response = await _proxy.InspectModelAsync(payload, ct);
        return response.Result;
    }

    /// <summary>
    /// Requests graceful daemon shutdown over RPC.
    /// </summary>
    public async Task ShutdownDaemonAsync(CancellationToken ct = default)
    {
        await _proxy.ShutdownAsync(ct);
    }

    public ValueTask DisposeAsync()
    {
        _rpc.Dispose();
        return ValueTask.CompletedTask;
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

        Console.Error.WriteLine($"[QL-DAEMON-CLIENT] {message}");
    }

    /// <summary>
    /// Builds the StreamJsonRpc message handler for the daemon channel.
    /// Uses 4-byte length-prefix framing with System.Text.Json camelCase serialization.
    /// Must be identical on both client and server.
    /// </summary>
    internal static IJsonRpcMessageHandler BuildMessageHandler(Stream stream)
    {
        var formatter = new SystemTextJsonFormatter
        {
            JsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web),
        };
        return new LengthHeaderMessageHandler(stream, stream, formatter);
    }
}
