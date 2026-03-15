using EFQueryLens.Core;
using EFQueryLens.DaemonClient;
using EFQueryLens.Lsp.Handlers;
using EFQueryLens.Lsp.Services;
using StreamJsonRpc;

namespace EFQueryLens.Lsp.Hosting;

internal static class MicrosoftLspHost
{
    public static async Task RunAsync(IQueryLensEngine engine)
    {
        var debugEnabled = ReadBoolEnvironmentVariable("QUERYLENS_DEBUG", fallback: false);
        if (debugEnabled)
            Console.Error.WriteLine("[QL-LSP] host-run debug=true");

        var documentManager = new DocumentManager();
        var hoverHandler = new HoverHandler(documentManager, new HoverPreviewService(engine));
        var lspHandler = new LanguageServerHandler(
            hover: hoverHandler,
            warmup: new WarmupHandler(documentManager, engine),
            daemonControl: new DaemonControlHandler(engine),
            textSync: new TextDocumentSyncHandler(documentManager),
            debugEnabled: debugEnabled);

        CancellationTokenSource? daemonEventCts = null;
        Task? daemonEventTask = null;

        if (engine is ResiliencyDaemonEngine resiliencyEngine)
        {
            daemonEventCts = new CancellationTokenSource();
            daemonEventTask = resiliencyEngine.RunDaemonEventSubscriptionAsync(
                hoverHandler.HandleDaemonEvent,
                daemonEventCts.Token);

            if (debugEnabled)
            {
                Console.Error.WriteLine("[QL-LSP] daemon-subscribe-loop started");
            }
        }

        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();

        var formatter = new JsonMessageFormatter();
        var msgHandler = new HeaderDelimitedMessageHandler(stdout, stdin, formatter);
        var rpc = new JsonRpc(msgHandler, lspHandler);
        lspHandler.JsonRpc = rpc;

        rpc.StartListening();
        if (debugEnabled)
            Console.Error.WriteLine("[QL-LSP] listening");

        try
        {
            await rpc.Completion;
        }
        finally
        {
            if (daemonEventCts is not null)
            {
                daemonEventCts.Cancel();
                if (daemonEventTask is not null)
                {
                    try
                    {
                        await daemonEventTask;
                    }
                    catch (OperationCanceledException) when (daemonEventCts.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex)
                    {
                        if (debugEnabled)
                        {
                            Console.Error.WriteLine($"[QL-LSP] daemon-subscribe-loop failed type={ex.GetType().Name} message={ex.Message}");
                        }
                    }
                }

                daemonEventCts.Dispose();
            }
        }
    }

    private static bool ReadBoolEnvironmentVariable(string variableName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (bool.TryParse(raw, out var parsed)) return parsed;
        return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}