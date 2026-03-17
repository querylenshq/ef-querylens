using EFQueryLens.Core;
using EFQueryLens.Core.Contracts;
using EFQueryLens.DaemonClient;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Handlers;
using EFQueryLens.Lsp.Services;
using StreamJsonRpc;

namespace EFQueryLens.Lsp.Hosting;

internal static class MicrosoftLspHost
{
    public static async Task RunAsync(IQueryLensEngine engine)
    {
        var debugEnabled = LspEnvironment.ReadBool("QUERYLENS_DEBUG", fallback: false);
        if (debugEnabled)
            Console.Error.WriteLine("[QL-LSP] host-run debug=true");

        var documentManager = new DocumentManager();
        var hoverHandler = new HoverHandler(documentManager, new HoverPreviewService(engine, debugEnabled));
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
}