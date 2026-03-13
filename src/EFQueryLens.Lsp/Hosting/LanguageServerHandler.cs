using EFQueryLens.Lsp.Handlers;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace EFQueryLens.Lsp.Hosting;

/// <summary>
/// All LSP method handlers registered with <see cref="JsonRpc"/>.
/// Methods returning <c>void</c> are notification handlers (no response).
/// Methods returning <c>Task&lt;T&gt;</c> are request handlers (response required).
/// StreamJsonRpc dispatches based on the <see cref="JsonRpcMethodAttribute"/> wire name.
/// </summary>
internal sealed class LanguageServerHandler
{
    private readonly HoverHandler _hover;
    private readonly CodeLensHandler _codeLens;
    private readonly InlayHintHandler _inlayHint;
    private readonly TextDocumentSyncHandler _textSync;
    private readonly bool _debugEnabled;
    private bool _shutdownRequested;

    /// <summary>
    /// Set immediately after <see cref="JsonRpc"/> construction so that
    /// <see cref="Exit"/> can dispose the connection.
    /// </summary>
    internal JsonRpc? JsonRpc { get; set; }

    public LanguageServerHandler(
        HoverHandler hover,
        CodeLensHandler codeLens,
        InlayHintHandler inlayHint,
        TextDocumentSyncHandler textSync,
        bool debugEnabled = false)
    {
        _hover = hover;
        _codeLens = codeLens;
        _inlayHint = inlayHint;
        _textSync = textSync;
        _debugEnabled = debugEnabled;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
    public JObject Initialize(JToken? _ = null) => CreateInitializeResult();

    [JsonRpcMethod("initialized")]
    public void Initialized() { }

    [JsonRpcMethod("shutdown")]
    public void Shutdown() => _shutdownRequested = true;

    [JsonRpcMethod("exit")]
    public void Exit()
    {
        if (!_shutdownRequested)
            Environment.ExitCode = 1;
        JsonRpc?.Dispose();
    }

    // ── Text document sync (notifications) ───────────────────────────────────

    [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
    public void DidOpen(DidOpenTextDocumentParams p) => _textSync.DidOpen(p);

    [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
    public void DidChange(DidChangeTextDocumentParams p) => _textSync.DidChange(p);

    [JsonRpcMethod("textDocument/didClose", UseSingleObjectParameterDeserialization = true)]
    public void DidClose(DidCloseTextDocumentParams p) => _textSync.DidClose(p);

    [JsonRpcMethod("textDocument/didSave", UseSingleObjectParameterDeserialization = true)]
    public void DidSave(DidSaveTextDocumentParams p) => _textSync.DidSave(p);

    [JsonRpcMethod("workspace/didChangeConfiguration", UseSingleObjectParameterDeserialization = true)]
    public void DidChangeConfiguration(JToken? _ = null) { }

    // ── Hover ─────────────────────────────────────────────────────────────────

    [JsonRpcMethod("textDocument/hover", UseSingleObjectParameterDeserialization = true)]
    public Task<Hover?> HoverAsync(TextDocumentPositionParams request, CancellationToken ct)
    {
        if (_debugEnabled) Console.Error.WriteLine($"[QL-LSP] request method=textDocument/hover");
        return _hover.HandleAsync(request, ct);
    }

    // ── Code lens ────────────────────────────────────────────────────────────

    [JsonRpcMethod("textDocument/codeLens", UseSingleObjectParameterDeserialization = true)]
    public async Task<CodeLens[]> CodeLensAsync(CodeLensParams request, CancellationToken ct)
    {
        if (_debugEnabled) Console.Error.WriteLine($"[QL-LSP] request method=textDocument/codeLens");
        var result = await _codeLens.HandleAsync(request, ct);
        return result ?? [];
    }

    [JsonRpcMethod("codeLens/resolve", UseSingleObjectParameterDeserialization = true)]
    public Task<CodeLens> CodeLensResolveAsync(CodeLens request, CancellationToken ct) =>
        _codeLens.ResolveAsync(request, ct);

    // ── Inlay hints ──────────────────────────────────────────────────────────

    [JsonRpcMethod("textDocument/inlayHint", UseSingleObjectParameterDeserialization = true)]
    public async Task<JObject[]> InlayHintAsync(JObject request, CancellationToken ct)
    {
        if (_debugEnabled) Console.Error.WriteLine($"[QL-LSP] request method=textDocument/inlayHint");
        var result = await _inlayHint.HandleAsync(request, ct);
        return result ?? [];
    }

    [JsonRpcMethod("inlayHint/resolve", UseSingleObjectParameterDeserialization = true)]
    public Task<JObject> InlayHintResolveAsync(JObject request, CancellationToken ct) =>
        _inlayHint.ResolveAsync(request, ct);

    // ── Server capabilities ──────────────────────────────────────────────────

    private static JObject CreateInitializeResult() => new()
    {
        ["capabilities"] = new JObject
        {
            ["textDocumentSync"] = new JObject
            {
                ["openClose"] = true,
                ["change"] = (int)TextDocumentSyncKind.Full,
                ["save"] = new JObject { ["includeText"] = true },
            },
            ["hoverProvider"] = true,
            ["codeLensProvider"] = new JObject { ["resolveProvider"] = false },
        },
    };
}
