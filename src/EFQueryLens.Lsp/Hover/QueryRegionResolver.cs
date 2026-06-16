using System.Collections.Concurrent;
using System.Diagnostics;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp.Parsing;

namespace EFQueryLens.Lsp.HoverPipeline;

internal sealed class QueryRegionResolver
{
    private sealed record RegisteredSpan(int SpanStart, int SpanEnd, string SemanticKey);

    private readonly DocumentLinqChainCache _chainCache;
    private readonly ConcurrentDictionary<string, List<RegisteredSpan>> _spanIndex = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly ConcurrentDictionary<string, QueryRegion> _regionBySemanticKey = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly ConcurrentDictionary<
        string,
        Lazy<Task<RegionResolveResult>>
    > _resolveInflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string>? _log;

    public QueryRegionResolver(DocumentLinqChainCache chainCache, Action<string>? log = null)
    {
        _chainCache = chainCache;
        _log = log;
    }

    public bool TryGetSemanticKeyByPosition(
        string filePath,
        string sourceText,
        int line,
        int character,
        out string? semanticKey
    )
    {
        semanticKey = null;
        if (!LspSyntaxHelper.TryGetAbsolutePosition(sourceText, line, character, out var position))
        {
            return false;
        }

        var normalizedPath = Path.GetFullPath(filePath);
        if (!_spanIndex.TryGetValue(normalizedPath, out var spans))
        {
            return false;
        }

        lock (spans)
        {
            foreach (var span in spans)
            {
                if (position >= span.SpanStart && position <= span.SpanEnd)
                {
                    semanticKey = span.SemanticKey;
                    return true;
                }
            }
        }

        return false;
    }

    public RegionResolveResult TryResolve(
        string filePath,
        string sourceText,
        int line,
        int character
    )
    {
        var sw = Stopwatch.StartNew();
        var inflightKey = BuildRegionInflightKey(filePath, sourceText, line, character);
        var created = new Lazy<Task<RegionResolveResult>>(
            () => Task.Run(() => ResolveCore(filePath, sourceText, line, character)),
            LazyThreadSafetyMode.ExecutionAndPublication
        );
        var inflight = _resolveInflight.GetOrAdd(inflightKey, created);
        var isOwner = ReferenceEquals(inflight, created);

        if (isOwner)
        {
            _ = inflight.Value.ContinueWith(
                _ =>
                    _resolveInflight.TryRemove(inflightKey, out Lazy<Task<RegionResolveResult>>? _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }

        var result = inflight.Value.GetAwaiter().GetResult();
        _log?.Invoke(
            $"semantic-resolve-finished line={line} char={character} found={result.Found} "
                + $"elapsedMs={sw.ElapsedMilliseconds} source={result.Source}"
        );
        return result;
    }

    public async Task<RegionResolveResult> TryResolveAsync(
        string filePath,
        string sourceText,
        int line,
        int character,
        CancellationToken cancellationToken
    )
    {
        var sw = Stopwatch.StartNew();
        var inflightKey = BuildRegionInflightKey(filePath, sourceText, line, character);
        var created = new Lazy<Task<RegionResolveResult>>(
            () =>
                Task.Run(
                    () => ResolveCore(filePath, sourceText, line, character),
                    cancellationToken
                ),
            LazyThreadSafetyMode.ExecutionAndPublication
        );
        var inflight = _resolveInflight.GetOrAdd(inflightKey, created);
        var isOwner = ReferenceEquals(inflight, created);

        if (isOwner)
        {
            _ = inflight.Value.ContinueWith(
                _ =>
                    _resolveInflight.TryRemove(inflightKey, out Lazy<Task<RegionResolveResult>>? _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }

        var result = await inflight.Value.ConfigureAwait(false);
        _log?.Invoke(
            $"semantic-resolve-finished line={line} char={character} found={result.Found} "
                + $"elapsedMs={sw.ElapsedMilliseconds} source={result.Source}"
        );
        return result;
    }

    public RegionResolveResult TryResolveFast(
        string filePath,
        string sourceText,
        int line,
        int character
    )
    {
        var sw = Stopwatch.StartNew();
        var result = ResolveCore(
            filePath,
            sourceText,
            line,
            character,
            allowCrossFileHelperLookup: false,
            allowDocumentChainScan: false
        );
        _log?.Invoke(
            $"semantic-fast-resolve-finished line={line} char={character} found={result.Found} "
                + $"elapsedMs={sw.ElapsedMilliseconds} source={result.Source}"
        );
        return result;
    }

    public bool MightNeedFullResolve(string filePath, string sourceText, int line, int character)
    {
        if (
            TryGetSemanticKeyByPosition(
                filePath,
                sourceText,
                line,
                character,
                out var registeredKey
            ) && !string.IsNullOrWhiteSpace(registeredKey)
        )
        {
            return true;
        }

        return LspSyntaxHelper.TryGetEnclosingLinqStatementSpan(
                sourceText,
                line,
                character,
                out _,
                out _
            )
            || LspSyntaxHelper.TryGetEnclosingInvocationSpan(
                sourceText,
                line,
                character,
                out _,
                out _
            )
            || IsDeclarationKeywordHover(sourceText, line, character);
    }

    public IReadOnlyList<string> BuildChainSemanticKeys(
        string filePath,
        string sourceText,
        IReadOnlyList<LinqChainInfo> chains
    )
    {
        var keys = new string[chains.Count];
        for (var i = 0; i < chains.Count; i++)
        {
            var chain = chains[i];
            var request = TranslationRequestBuilder.TryBuild(
                filePath,
                sourceText,
                chain.Expression,
                chain.ContextVariableName,
                chain.Line,
                chain.Character
            );
            keys[i] = request is not null
                ? BuildSemanticKey(request)
                : $"unresolved|{chain.Line}|{chain.Character}|{NormalizeWhitespace(chain.Expression)}";
        }

        return keys;
    }

    public static string BuildSemanticKey(TranslationRequest request) =>
        TranslationRequestBuilder.BuildSemanticCacheKey(request);

    public static string BuildRegionInflightKey(
        string filePath,
        string sourceText,
        int line,
        int character
    )
    {
        var normalizedPath = Path.GetFullPath(filePath);
        if (
            LspSyntaxHelper.TryGetEnclosingLinqStatementSpan(
                sourceText,
                line,
                character,
                out var statementStart,
                out var statementEnd
            )
        )
        {
            return $"{normalizedPath}|stmt|{statementStart}|{statementEnd}";
        }

        if (
            LspSyntaxHelper.TryGetEnclosingInvocationSpan(
                sourceText,
                line,
                character,
                out var invocationStart,
                out var invocationEnd
            )
        )
        {
            return $"{normalizedPath}|inv|{invocationStart}|{invocationEnd}";
        }

        return $"{normalizedPath}|pos|{line}|{character}";
    }

    public void Clear()
    {
        _spanIndex.Clear();
        _regionBySemanticKey.Clear();
        _resolveInflight.Clear();
    }

    private RegionResolveResult ResolveCore(
        string filePath,
        string sourceText,
        int line,
        int character,
        bool allowCrossFileHelperLookup = true,
        bool allowDocumentChainScan = true
    )
    {
        if (
            TryGetSemanticKeyByPosition(
                filePath,
                sourceText,
                line,
                character,
                out var registeredKey
            )
            && !string.IsNullOrWhiteSpace(registeredKey)
            && _regionBySemanticKey.TryGetValue(registeredKey!, out var registeredRegion)
        )
        {
            return new RegionResolveResult(
                true,
                registeredRegion.WithRequestPosition(line, character),
                "span-registry"
            );
        }

        var expression = LspSyntaxHelper.TryExtractLinqExpression(
            sourceText,
            line,
            character,
            out var contextVariableName,
            sourceFilePath: filePath,
            allowCrossFileHelperLookup: allowCrossFileHelperLookup
        );

        if (
            !string.IsNullOrWhiteSpace(expression)
            && !string.IsNullOrWhiteSpace(contextVariableName)
        )
        {
            var region = CreateRegion(
                filePath,
                sourceText,
                expression,
                contextVariableName,
                line,
                character
            );
            if (region is not null)
            {
                RememberRegion(filePath, sourceText, line, character, region);
                return new RegionResolveResult(
                    true,
                    region.WithRequestPosition(line, character),
                    "extract-linq"
                );
            }

            return new RegionResolveResult(false, null, "extract-linq");
        }

        if (!allowDocumentChainScan)
        {
            return new RegionResolveResult(false, null, "fast-none");
        }

        var chains = _chainCache.GetOrFindChains(filePath, sourceText);
        if (
            TryFindChainByExpressionSpan(chains, line, character, out var expressionChain)
            || (
                IsDeclarationKeywordHover(sourceText, line, character)
                && TryFindContainingChainByStatement(chains, line, character, out expressionChain)
            )
        )
        {
            var region = CreateRegion(
                filePath,
                sourceText,
                expressionChain.Expression,
                expressionChain.ContextVariableName,
                expressionChain.Line,
                expressionChain.Character
            );
            if (region is not null)
            {
                RememberRegion(filePath, sourceText, line, character, region);
                return new RegionResolveResult(
                    true,
                    region.WithRequestPosition(line, character),
                    "chain-span"
                );
            }

            return new RegionResolveResult(false, null, "chain-span");
        }

        return new RegionResolveResult(false, null, "none");
    }

    private void RememberRegion(
        string filePath,
        string sourceText,
        int line,
        int character,
        QueryRegion region
    )
    {
        _regionBySemanticKey[region.SemanticKey] = region;
        RegisterSpans(filePath, sourceText, line, character, region.SemanticKey);
    }

    private void RegisterSpans(
        string filePath,
        string sourceText,
        int line,
        int character,
        string semanticKey
    )
    {
        if (
            LspSyntaxHelper.TryGetEnclosingCallArgumentSpan(
                sourceText,
                line,
                character,
                out var argumentStart,
                out var argumentEnd
            )
        )
        {
            AddSpan(filePath, argumentStart, argumentEnd, semanticKey);
        }

        if (
            LspSyntaxHelper.TryGetEnclosingInvocationSpan(
                sourceText,
                line,
                character,
                out var invocationStart,
                out var invocationEnd
            )
        )
        {
            AddSpan(filePath, invocationStart, invocationEnd, semanticKey);
        }

        if (
            LspSyntaxHelper.TryGetEnclosingLinqStatementSpan(
                sourceText,
                line,
                character,
                out var statementStart,
                out var statementEnd
            )
        )
        {
            AddSpan(filePath, statementStart, statementEnd, semanticKey);
        }
    }

    private void AddSpan(string filePath, int spanStart, int spanEnd, string semanticKey)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        var spans = _spanIndex.GetOrAdd(normalizedPath, _ => []);
        lock (spans)
        {
            foreach (var existing in spans)
            {
                if (
                    string.Equals(existing.SemanticKey, semanticKey, StringComparison.Ordinal)
                    && existing.SpanStart == spanStart
                    && existing.SpanEnd == spanEnd
                )
                {
                    return;
                }
            }

            spans.Add(new RegisteredSpan(spanStart, spanEnd, semanticKey));
        }
    }

    private QueryRegion? CreateRegion(
        string filePath,
        string sourceText,
        string expression,
        string contextVariableName,
        int anchorLine,
        int anchorCharacter
    )
    {
        var request = TranslationRequestBuilder.TryBuild(
            filePath,
            sourceText,
            expression,
            contextVariableName,
            anchorLine,
            anchorCharacter
        );
        if (request is null)
        {
            return null;
        }

        var fingerprint =
            AssemblyResolver.TryGetAssemblyFingerprint(filePath)
            ?? $"no-assembly|{Path.GetFullPath(filePath)}";
        var semanticKey = BuildSemanticKey(request);
        var regionKey = BuildRegionInflightKey(filePath, sourceText, anchorLine, anchorCharacter);

        return new QueryRegion(
            semanticKey,
            regionKey,
            fingerprint,
            anchorLine,
            anchorCharacter,
            expression,
            contextVariableName
        );
    }

    private static bool TryFindChainByExpressionSpan(
        IReadOnlyList<LinqChainInfo> chains,
        int line,
        int character,
        out LinqChainInfo chain
    )
    {
        foreach (var candidate in chains)
        {
            if (IsWithinExpressionRange(candidate, line, character))
            {
                chain = candidate;
                return true;
            }
        }

        chain = null!;
        return false;
    }

    private static bool TryFindContainingChainByStatement(
        IReadOnlyList<LinqChainInfo> chains,
        int line,
        int character,
        out LinqChainInfo containingChain
    )
    {
        containingChain = null!;
        foreach (var chain in chains)
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

    private static bool IsWithinExpressionRange(LinqChainInfo chain, int line, int character)
    {
        if (line < chain.StatementStartLine || line > chain.StatementEndLine)
        {
            return false;
        }

        if (chain.StatementStartLine == chain.StatementEndLine)
        {
            return character >= chain.StatementStartCharacter
                && character <= chain.StatementEndCharacter;
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

    private static bool IsWithinStatementRange(LinqChainInfo chain, int line, int character)
    {
        if (line < chain.StatementStartLine || line > chain.StatementEndLine)
        {
            return false;
        }

        if (chain.StatementStartLine == chain.StatementEndLine)
        {
            return character >= chain.StatementStartCharacter
                && character <= chain.StatementEndCharacter;
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

    private static bool IsDeclarationKeywordHover(string sourceText, int line, int character)
    {
        var lines = sourceText.Split('\n');
        if (line >= lines.Length)
        {
            return false;
        }

        var textLine = lines[line];
        if (character > textLine.Length)
        {
            return false;
        }

        var prefix = textLine[..character];
        return prefix.Contains("var ", StringComparison.Ordinal)
            || prefix.Contains("await ", StringComparison.Ordinal)
            || prefix.TrimEnd().EndsWith('=');
    }

    internal static string NormalizeWhitespace(string value)
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

    internal sealed record RegionResolveResult(bool Found, QueryRegion? Region, string Source);
}
