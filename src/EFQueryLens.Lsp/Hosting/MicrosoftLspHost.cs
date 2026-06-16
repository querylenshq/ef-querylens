using EFQueryLens.Core.Contracts;
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

        var statusTracker = new QueryLensStatusTracker();
        statusTracker.SetDaemonReady(ready: true);

        var documentManager = new DocumentManager();
        var hoverPreviewService = new HoverPreviewService(engine, debugEnabled);
        var warmupHandler = new WarmupHandler(documentManager, engine);
        warmupHandler.SetStatusTracker(statusTracker);

        var hoverHandler = new HoverHandler(documentManager, hoverPreviewService, engine);
        hoverHandler.SetWarmupHandler(warmupHandler);
        hoverHandler.SetStatusTracker(statusTracker);

        var assemblyChangeTracker = new AssemblyChangeTracker(hoverHandler);
        hoverHandler.SetAssemblyChangeTracker(assemblyChangeTracker);
        warmupHandler.SetAssemblyChangeTracker(assemblyChangeTracker);
        var prewarm = new TranslationPrewarmService(
            hoverPreviewService,
            hoverHandler.BuildChainSemanticKeys,
            hoverHandler.IsSemanticKeyReady);
        prewarm.SetStatusTracker(statusTracker);

        var lspHandler = new LanguageServerHandler(
            hover: hoverHandler,
            warmup: warmupHandler,
            daemonControl: new DaemonControlHandler(engine),
            textSync: new TextDocumentSyncHandler(documentManager, prewarm, assemblyChangeTracker, hoverHandler),
            debugEnabled: debugEnabled);

        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();

        var formatter = new JsonMessageFormatter();
        var msgHandler = new HeaderDelimitedMessageHandler(stdout, stdin, formatter);
        var rpc = new JsonRpc(msgHandler, lspHandler);
        lspHandler.JsonRpc = rpc;
        lspHandler.SetStatusTracker(statusTracker);

        rpc.StartListening();
        if (debugEnabled)
            Console.Error.WriteLine("[QL-LSP] listening");

        await rpc.Completion;
    }
}
