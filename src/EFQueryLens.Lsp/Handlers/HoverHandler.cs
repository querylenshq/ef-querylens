using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using EFQueryLens.Lsp.Parsing;
using EFQueryLens.Lsp.Services;

namespace EFQueryLens.Lsp.Handlers;

internal sealed class HoverHandler
{
    private sealed record MarkedStringOrString(string? First, MarkedString? Second)
    {
        public static implicit operator SumType<string, MarkedString>(MarkedStringOrString value) =>
            value.Second is not null
                ? new SumType<string, MarkedString>(value.Second)
                : new SumType<string, MarkedString>(value.First ?? string.Empty);
    }

    private readonly DocumentManager _documentManager;
    private readonly HoverPreviewService _hoverPreviewService;
    private readonly ConcurrentDictionary<string, CachedHoverResult> _hoverCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedHoverResult> _semanticHoverCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<Hover?>>> _inflightSemanticHover = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _hoverCacheTtlMs;
    private readonly int _hoverCancellationGraceMs;
    private readonly bool _debugEnabled;

    public HoverHandler(DocumentManager documentManager, HoverPreviewService hoverPreviewService)
    {
        _documentManager = documentManager;
        _hoverPreviewService = hoverPreviewService;
        _hoverCacheTtlMs = ReadIntEnvironmentVariable(
            "QUERYLENS_HOVER_CACHE_TTL_MS",
            fallback: 15_000,
            min: 0,
            max: 120_000);
        _hoverCancellationGraceMs = ReadIntEnvironmentVariable(
            "QUERYLENS_HOVER_CANCEL_GRACE_MS",
            fallback: 350,
            min: 0,
            max: 5_000);
        _debugEnabled = ReadBoolEnvironmentVariable("QUERYLENS_DEBUG", fallback: false);
    }

    public async Task<Hover?> HandleAsync(TextDocumentPositionParams request, CancellationToken cancellationToken)
    {
        var filePath = DocumentPathResolver.Resolve(request.TextDocument.Uri);
        var documentUri = request.TextDocument.Uri.ToString();
        var sourceText = await GetSourceTextAsync(documentUri, filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return null;
        }

        LogHoverDebug($"hover-request path={filePath} line={request.Position.Line} char={request.Position.Character}");
        var hasSemanticContext = TryResolveSemanticHoverContext(
            sourceText,
            request.Position.Line,
            request.Position.Character,
            out var semanticContext);

        var effectiveLine = hasSemanticContext ? semanticContext!.EffectiveLine : request.Position.Line;
        var effectiveCharacter = hasSemanticContext ? semanticContext!.EffectiveCharacter : request.Position.Character;

        if (effectiveLine != request.Position.Line || effectiveCharacter != request.Position.Character)
        {
            LogHoverDebug(
                $"hover-normalized from line={request.Position.Line} char={request.Position.Character} " +
                $"to line={effectiveLine} char={effectiveCharacter}");
        }

        var cacheKey = BuildHoverCacheKey(
            filePath,
            sourceText,
            request.Position.Line,
            request.Position.Character,
            semanticContext);
        if (TryGetCachedHover(cacheKey, out var cachedHover))
        {
            LogHoverDebug($"hover-cache-hit line={effectiveLine} char={effectiveCharacter}");
            return cachedHover;
        }

        if (semanticContext is not null && TryGetSemanticCachedHover(semanticContext.SemanticKey, out var semanticCachedHover))
        {
            LogHoverDebug($"hover-semantic-cache-hit line={effectiveLine} char={effectiveCharacter}");
            CacheHover(cacheKey, semanticCachedHover, semanticContext);
            return semanticCachedHover;
        }

        if (semanticContext is not null)
        {
            var inFlightKey = BuildInFlightKey(filePath, semanticContext);
            var lazyTask = _inflightSemanticHover.GetOrAdd(
                inFlightKey,
                _ => new Lazy<Task<Hover?>>(
                    () => ComputeAndCacheSemanticHoverAsync(
                        inFlightKey,
                        cacheKey,
                        filePath,
                        sourceText,
                        semanticContext),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            if (lazyTask.IsValueCreated)
            {
                LogHoverDebug($"hover-inflight-join line={effectiveLine} char={effectiveCharacter}");
            }
            else
            {
                LogHoverDebug($"hover-inflight-start line={effectiveLine} char={effectiveCharacter}");
            }

            try
            {
                var sharedTask = lazyTask.Value;
                var sharedResult = await WaitWithCancellationAsync(sharedTask, cancellationToken);
                CacheHover(cacheKey, sharedResult, semanticContext);
                return sharedResult;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var sharedTask = lazyTask.Value;
                var (completed, salvagedResult) = await TryGetResultWithinGraceAsync(sharedTask, _hoverCancellationGraceMs);
                if (completed)
                {
                    CacheHover(cacheKey, salvagedResult, semanticContext);
                    LogHoverDebug(
                        $"hover-cancel-salvaged line={effectiveLine} char={effectiveCharacter} " +
                        $"graceMs={_hoverCancellationGraceMs}");
                    return salvagedResult;
                }

                if (TryGetSemanticCachedHover(semanticContext.SemanticKey, out var semanticCachedHoverAfterCancel))
                {
                    CacheHover(cacheKey, semanticCachedHoverAfterCancel, semanticContext);
                    LogHoverDebug($"hover-cancel-cache-hit line={effectiveLine} char={effectiveCharacter}");
                    return semanticCachedHoverAfterCancel;
                }

                LogHoverDebug($"hover-canceled line={effectiveLine} char={effectiveCharacter} reason=request-cancelled");
                return null;
            }
        }

        var sw = Stopwatch.StartNew();
        Hover? computed;
        try
        {
            computed = await ComputeHoverAsync(
                filePath,
                sourceText,
                effectiveLine,
                effectiveCharacter,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogHoverDebug($"hover-canceled line={effectiveLine} char={effectiveCharacter}");

            // Rider may cancel fast hover requests before translation completes.
            // Warm cache asynchronously so the next hover at the same semantic query can return immediately.
            if (semanticContext is not null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var warmed = await ComputeHoverAsync(
                            filePath,
                            sourceText,
                            semanticContext.EffectiveLine,
                            semanticContext.EffectiveCharacter,
                            CancellationToken.None);
                        CacheHover(cacheKey, warmed, semanticContext);
                        LogHoverDebug($"hover-warm-cache-ready line={semanticContext.EffectiveLine} char={semanticContext.EffectiveCharacter}");
                    }
                    catch (Exception ex)
                    {
                        LogHoverDebug($"hover-warm-cache-failed type={ex.GetType().Name} message={ex.Message}");
                    }
                });
            }

            return null;
        }
        sw.Stop();

        LogHoverDebug($"hover-compute-finished line={effectiveLine} char={effectiveCharacter} elapsedMs={sw.ElapsedMilliseconds} hasResult={computed is not null}");

        CacheHover(cacheKey, computed, semanticContext);
        return computed;
    }

    private async Task<Hover?> ComputeAndCacheSemanticHoverAsync(
        string inFlightKey,
        string cacheKey,
        string filePath,
        string sourceText,
        SemanticHoverContext semanticContext)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var computed = await ComputeHoverAsync(
                filePath,
                sourceText,
                semanticContext.EffectiveLine,
                semanticContext.EffectiveCharacter,
                CancellationToken.None);
            sw.Stop();

            LogHoverDebug(
                $"hover-compute-finished line={semanticContext.EffectiveLine} char={semanticContext.EffectiveCharacter} " +
                $"elapsedMs={sw.ElapsedMilliseconds} hasResult={computed is not null}");

            CacheHover(cacheKey, computed, semanticContext);
            return computed;
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogHoverDebug(
                $"hover-compute-failed line={semanticContext.EffectiveLine} char={semanticContext.EffectiveCharacter} " +
                $"elapsedMs={sw.ElapsedMilliseconds} type={ex.GetType().Name} message={ex.Message}");
            throw;
        }
        finally
        {
            _inflightSemanticHover.TryRemove(inFlightKey, out _);
        }
    }

    private async Task<string?> GetSourceTextAsync(string documentUri, string filePath, CancellationToken cancellationToken)
    {
        var sourceText = _documentManager.GetDocumentText(documentUri);
        if (!string.IsNullOrWhiteSpace(sourceText))
        {
            return sourceText;
        }

        if (!File.Exists(filePath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(filePath, cancellationToken);
    }

    private async Task<Hover?> ComputeHoverAsync(
        string filePath,
        string sourceText,
        int line,
        int character,
        CancellationToken cancellationToken)
    {
        var result = await _hoverPreviewService.BuildMarkdownAsync(
            filePath,
            sourceText,
            line,
            character,
            cancellationToken);

        if (!result.Success &&
            result.Output.StartsWith("Could not extract a LINQ query expression", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var markdown = result.Success
            ? result.Output
            : $"**QueryLens Error**\n```text\n{result.Output}\n```";

        var content = new MarkupContent
        {
            Kind = MarkupKind.Markdown,
            Value = markdown,
        };

        return new Hover
        {
            Contents = new SumType<SumType<string, MarkedString>, SumType<string, MarkedString>[], MarkupContent>(content),
        };
    }

    private string BuildHoverCacheKey(
        string filePath,
        string sourceText,
        int requestLine,
        int requestCharacter,
        SemanticHoverContext? semanticContext)
    {
        var sourceHash = StringComparer.Ordinal.GetHashCode(sourceText);

        if (semanticContext is not null)
        {
            return $"{Path.GetFullPath(filePath)}|semantic|{semanticContext.SemanticKey}|{semanticContext.EffectiveLine}|{semanticContext.EffectiveCharacter}|{sourceHash}";
        }

        return $"{Path.GetFullPath(filePath)}|cursor|{requestLine}|{requestCharacter}|{sourceHash}";
    }

    private static bool TryResolveSemanticHoverContext(
        string sourceText,
        int line,
        int character,
        out SemanticHoverContext? semanticContext)
    {
        semanticContext = null;

        if (TryFindContainingChain(sourceText, line, character, out var containingChain))
        {
            semanticContext = new SemanticHoverContext(
                SemanticKey: $"{containingChain.ContextVariableName.Trim()}|{NormalizeWhitespace(containingChain.Expression)}",
                EffectiveLine: containingChain.Line,
                EffectiveCharacter: containingChain.Character);
            return true;
        }

        var expression = LspSyntaxHelper.TryExtractLinqExpression(sourceText, line, character, out var contextVariableName);
        if (string.IsNullOrWhiteSpace(expression) || string.IsNullOrWhiteSpace(contextVariableName))
        {
            var fallback = LspSyntaxHelper.FindAllLinqChains(sourceText)
                .OrderBy(chain => Math.Abs(chain.Line - line))
                .ThenBy(chain => Math.Abs(chain.Character - character))
                .FirstOrDefault();

            if (fallback is not null)
            {
                expression = fallback.Expression;
                contextVariableName = fallback.ContextVariableName;
                line = fallback.Line;
                character = fallback.Character;
            }
        }

        if (string.IsNullOrWhiteSpace(expression) || string.IsNullOrWhiteSpace(contextVariableName))
        {
            return false;
        }

        semanticContext = new SemanticHoverContext(
            SemanticKey: $"{contextVariableName.Trim()}|{NormalizeWhitespace(expression)}",
            EffectiveLine: line,
            EffectiveCharacter: character);
        return true;
    }

    private static bool TryFindContainingChain(string sourceText, int line, int character, out LinqChainInfo containingChain)
    {
        containingChain = null!;

        foreach (var chain in LspSyntaxHelper.FindAllLinqChains(sourceText))
        {
            if (!IsWithinStatementRange(chain, line, character))
            {
                continue;
            }

            containingChain = chain;
            return true;
        }

        return false;
    }

    private static bool IsWithinStatementRange(LinqChainInfo chain, int line, int character)
    {
        if (line < chain.StatementStartLine || line > chain.StatementEndLine)
        {
            return false;
        }

        if (chain.StatementStartLine == chain.StatementEndLine)
        {
            return character >= chain.StatementStartCharacter && character <= chain.StatementEndCharacter;
        }

        if (line == chain.StatementStartLine)
        {
            return character >= chain.StatementStartCharacter;
        }

        if (line == chain.StatementEndLine)
        {
            return character <= chain.StatementEndCharacter;
        }

        return true;
    }

    private static string NormalizeWhitespace(string value)
    {
        var buffer = new char[value.Length];
        var index = 0;
        var previousWasWhitespace = false;

        foreach (var current in value)
        {
            if (char.IsWhiteSpace(current))
            {
                if (previousWasWhitespace)
                {
                    continue;
                }

                buffer[index++] = ' ';
                previousWasWhitespace = true;
            }
            else
            {
                buffer[index++] = current;
                previousWasWhitespace = false;
            }
        }

        return new string(buffer, 0, index).Trim();
    }

    private bool TryGetCachedHover(string cacheKey, out Hover? hover)
    {
        hover = null;

        if (_hoverCacheTtlMs <= 0)
        {
            return false;
        }

        if (!_hoverCache.TryGetValue(cacheKey, out var cached))
        {
            return false;
        }

        var expiresAtTicks = cached.CreatedAtTicks + TimeSpan.FromMilliseconds(_hoverCacheTtlMs).Ticks;
        if (expiresAtTicks <= DateTime.UtcNow.Ticks)
        {
            _hoverCache.TryRemove(cacheKey, out _);
            return false;
        }

        hover = cached.Hover;
        return true;
    }

    private bool TryGetSemanticCachedHover(string semanticKey, out Hover? hover)
    {
        hover = null;

        if (_hoverCacheTtlMs <= 0)
        {
            return false;
        }

        if (!_semanticHoverCache.TryGetValue(semanticKey, out var cached))
        {
            return false;
        }

        var expiresAtTicks = cached.CreatedAtTicks + TimeSpan.FromMilliseconds(_hoverCacheTtlMs).Ticks;
        if (expiresAtTicks <= DateTime.UtcNow.Ticks)
        {
            _semanticHoverCache.TryRemove(semanticKey, out _);
            return false;
        }

        hover = cached.Hover;
        return hover is not null;
    }

    private static async Task<T> WaitWithCancellationAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return await task;
        }

        if (task.IsCompleted)
        {
            return await task;
        }

        var cancellationTaskSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            cancellationTaskSource);

        var completed = await Task.WhenAny(task, cancellationTaskSource.Task);
        if (completed != task)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return await task;
    }

    private static async Task<(bool Completed, T? Result)> TryGetResultWithinGraceAsync<T>(Task<T> task, int graceMilliseconds)
    {
        if (graceMilliseconds <= 0)
        {
            return (false, default);
        }

        if (task.IsCompleted)
        {
            try
            {
                return (true, await task);
            }
            catch
            {
                return (false, default);
            }
        }

        var completed = await Task.WhenAny(task, Task.Delay(graceMilliseconds));
        if (completed != task)
        {
            return (false, default);
        }

        try
        {
            return (true, await task);
        }
        catch
        {
            return (false, default);
        }
    }

    private static string BuildInFlightKey(string filePath, SemanticHoverContext semanticContext) =>
        $"{Path.GetFullPath(filePath)}|{semanticContext.SemanticKey}|{semanticContext.EffectiveLine}|{semanticContext.EffectiveCharacter}";

    private void CacheHover(string cacheKey, Hover? hover, SemanticHoverContext? semanticContext)
    {
        if (_hoverCacheTtlMs <= 0)
        {
            return;
        }

        _hoverCache[cacheKey] = new CachedHoverResult(DateTime.UtcNow.Ticks, hover);

        if (semanticContext is not null && hover is not null)
        {
            _semanticHoverCache[semanticContext.SemanticKey] = new CachedHoverResult(DateTime.UtcNow.Ticks, hover);
        }

        if (_hoverCache.Count > 2_000)
        {
            _hoverCache.Clear();
        }

        if (_semanticHoverCache.Count > 2_000)
        {
            _semanticHoverCache.Clear();
        }
    }

    private static int ReadIntEnvironmentVariable(string variableName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);
        if (!int.TryParse(raw, out var value))
        {
            return fallback;
        }

        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
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

    private void LogHoverDebug(string message)
    {
        if (!_debugEnabled)
        {
            return;
        }

        Console.Error.WriteLine($"[QL-Hover] {message}");
    }

    private sealed record SemanticHoverContext(string SemanticKey, int EffectiveLine, int EffectiveCharacter);

    private sealed record CachedHoverResult(long CreatedAtTicks, Hover? Hover);
}
