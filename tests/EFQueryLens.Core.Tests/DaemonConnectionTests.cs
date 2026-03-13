using System.IO.Pipes;
using System.Text.Json;
using System.Collections.Concurrent;
using EFQueryLens.Core;
using EFQueryLens.Core.Daemon;
using EFQueryLens.DaemonClient;
using StreamJsonRpc;

namespace EFQueryLens.Core.Tests;

/// <summary>
/// Tests for DaemonBackedEngine transport lifecycle behaviors.
/// </summary>
public class DaemonBackedEngineTests
{
    private static IJsonRpcMessageHandler BuildHandler(Stream stream) =>
        new LengthHeaderMessageHandler(stream, stream,
            new SystemTextJsonFormatter
            {
                JsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web),
            });

    [Fact]
    public async Task TranslateAsync_StubServer_ReturnsResult()
    {
        var pipeName = $"ql-test-{Guid.NewGuid():N}";

        var serverTask = Task.Run(async () =>
        {
            await using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync();

            var service = new StubDaemonService(success: false, errorMessage: "stub-error");
            var rpc = new JsonRpc(BuildHandler(server), service);
            rpc.StartListening();
            await rpc.Completion;
        });

        await using var clientPipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await clientPipe.ConnectAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        await using var engine = new DaemonBackedEngine(clientPipe, "test");

        var result = await engine.TranslateAsync(new TranslationRequest
        {
            AssemblyPath = "test.dll",
            Expression = "ctx.Users",
            ContextVariableName = "ctx",
        });

        Assert.False(result.Success);
        Assert.Equal("stub-error", result.ErrorMessage);

        await engine.DisposeAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TranslateAsync_ServerDisconnects_Throws()
    {
        var pipeName = $"ql-test-{Guid.NewGuid():N}";

        var serverTask = Task.Run(async () =>
        {
            await using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync();
            // Immediate dispose simulates daemon crash.
        });

        await using var clientPipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await clientPipe.ConnectAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        await using var engine = new DaemonBackedEngine(clientPipe, "test");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            engine.TranslateAsync(new TranslationRequest
            {
                AssemblyPath = "test.dll",
                Expression = "ctx.Users",
                ContextVariableName = "ctx",
            }, new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token));

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ResiliencyEngine_ConnectionLost_ReconnectsAndRetries()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "ql-reconnect-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);

        var firstPipeName = $"ql-test-first-{Guid.NewGuid():N}";
        var secondPipeName = $"ql-test-second-{Guid.NewGuid():N}";

        var pidFilePath = DaemonWorkspaceIdentity.BuildPidFilePath(workspacePath);
        Directory.CreateDirectory(Path.GetDirectoryName(pidFilePath)!);
        await File.WriteAllTextAsync(
            pidFilePath,
            JsonSerializer.Serialize(new
            {
                ProcessId = Environment.ProcessId,
                PipeName = secondPipeName,
                WorkspacePath = workspacePath,
            }));

        var reconnectLogs = new ConcurrentQueue<string>();

        var firstServerTask = Task.Run(async () =>
        {
            await using var server = new NamedPipeServerStream(
                firstPipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await server.WaitForConnectionAsync();
            await Task.Delay(50);
            // Disposing the transport simulates daemon crash/disconnect.
        });

        var secondServerTask = Task.Run(async () =>
        {
            await using var server = new NamedPipeServerStream(
                secondPipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await server.WaitForConnectionAsync();

            var service = new StubDaemonService(success: true, errorMessage: null, sql: "SELECT 1");
            var rpc = new JsonRpc(BuildHandler(server), service);
            rpc.StartListening();
            await rpc.Completion;
        });

        await using var clientPipe = new NamedPipeClientStream(
            ".", firstPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await clientPipe.ConnectAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        var engine = new ResiliencyDaemonEngine(
            new DaemonBackedEngine(clientPipe, "test"),
            workspacePath,
            daemonExecutablePath: null,
            daemonAssemblyPath: null,
            contextName: "test",
            connectTimeoutMs: 3000,
            startTimeoutMs: 3000,
            debugLog: message => reconnectLogs.Enqueue(message));

        try
        {
            var result = await engine.TranslateAsync(new TranslationRequest
            {
                AssemblyPath = "test.dll",
                Expression = "ctx.Users",
                ContextVariableName = "ctx",
            }, new CancellationTokenSource(TimeSpan.FromSeconds(8)).Token);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains(reconnectLogs, message =>
                message.Contains("will-reconnect", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(reconnectLogs, message =>
                message.Contains("daemon-reconnect success", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await engine.DisposeAsync();
            await firstServerTask.WaitAsync(TimeSpan.FromSeconds(5));
            await secondServerTask.WaitAsync(TimeSpan.FromSeconds(5));

            try
            {
                if (File.Exists(pidFilePath))
                {
                    File.Delete(pidFilePath);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }

            try
            {
                if (Directory.Exists(workspacePath))
                {
                    Directory.Delete(workspacePath, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private sealed class StubDaemonService
    {
        private readonly bool _success;
        private readonly string? _errorMessage;
        private readonly string? _sql;

        public StubDaemonService(bool success, string? errorMessage, string? sql = null)
        {
            _success = success;
            _errorMessage = errorMessage;
            _sql = sql;
        }

        [JsonRpcMethod(DaemonMethods.Translate)]
        public Task<DaemonTranslateResponse> TranslateAsync(DaemonTranslateRequest request, CancellationToken cancellationToken)
        {
            var result = new QueryTranslationResult
            {
                Success = _success,
                ErrorMessage = _errorMessage,
                Sql = _sql,
            };
            return Task.FromResult(new DaemonTranslateResponse { Result = result });
        }
    }
}