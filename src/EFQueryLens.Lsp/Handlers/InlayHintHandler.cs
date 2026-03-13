using System.Collections.Concurrent;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using EFQueryLens.Lsp.Parsing;
using EFQueryLens.Lsp.Services;
using Newtonsoft.Json.Linq;

namespace EFQueryLens.Lsp.Handlers;

/// <summary>
/// Handles textDocument/inlayHint requests for Rider 2025.2+.
/// Provides inline SQL preview annotations instead of CodeLens (which Rider doesn't support).
/// </summary>
internal sealed class InlayHintHandler
{
    private const int DefaultMaxInlayHintsPerDocument = 50;
    private const int DefaultDebounceMilliseconds = 250;

    private readonly DocumentManager _documentManager;
    private readonly CodeLensPreviewService _previewService;
    private readonly int _maxHintsPerDocument;
    private readonly int _debounceMilliseconds;
    private readonly bool _debugEnabled;
    private readonly bool _forceInlayHints;
    private readonly ConcurrentDictionary<string, CachedInlayHintResult> _hintCache =
        new(StringComparer.OrdinalIgnoreCase);

    public InlayHintHandler(DocumentManager documentManager, CodeLensPreviewService previewService)
    {
        _documentManager = documentManager;
        _previewService = previewService;
        _maxHintsPerDocument = ReadIntEnvironmentVariable(
            "QUERYLENS_MAX_INLAY_HINTS_PER_DOCUMENT",
            DefaultMaxInlayHintsPerDocument,
            min: 1,
            max: 500);
        _debounceMilliseconds = ReadIntEnvironmentVariable(
            "QUERYLENS_INLAY_HINTS_DEBOUNCE_MS",
            DefaultDebounceMilliseconds,
            min: 0,
            max: 5000);
        _debugEnabled = ReadBoolEnvironmentVariable("QUERYLENS_DEBUG", fallback: false);
        _forceInlayHints = ReadBoolEnvironmentVariable("QUERYLENS_FORCE_INLAY_HINTS", fallback: false);

        LogDebug($"initialized max={_maxHintsPerDocument} debounceMs={_debounceMilliseconds} forceInlayHints={_forceInlayHints}");
    }

    public async Task<JObject[]?> HandleAsync(JObject request, CancellationToken cancellationToken)
    {
        try
        {
            var textDocumentToken = request["textDocument"];
            if (textDocumentToken is null)
            {
                LogDebug("handle-exit reason=no-textDocument");
                return [];
            }

            var uriText = textDocumentToken["uri"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(uriText))
            {
                LogDebug("handle-exit reason=no-uri");
                return [];
            }

            var filePath = DocumentPathResolver.Resolve(new Uri(uriText));
            LogDebug($"handle-start uri={uriText} path={filePath}");
            
            var sourceText = await GetSourceTextAsync(uriText, filePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(sourceText))
            {
                LogDebug("handle-exit reason=empty-source");
                return [];
            }

            var sourceHash = StringComparer.Ordinal.GetHashCode(sourceText);
            var nowTicks = DateTime.UtcNow.Ticks;
            if (_debounceMilliseconds > 0
                && _hintCache.TryGetValue(filePath, out var cachedResult)
                && cachedResult.SourceHash == sourceHash
                && cachedResult.CreatedAtTicks + TimeSpan.FromMilliseconds(_debounceMilliseconds).Ticks > nowTicks)
            {
                LogDebug($"handle-exit reason=cache-hit count={cachedResult.Hints.Length}");
                return cachedResult.Hints;
            }

            var anchors = _previewService.ComputeAnchors(filePath, sourceText, _maxHintsPerDocument);
            if (anchors.Count == 0)
            {
                LogDebug("handle-exit reason=no-anchors");
                return [];
            }

            var hints = new List<JObject>();
            foreach (var anchor in anchors)
            {
                var commandArgs = new JArray
                {
                    uriText,
                    anchor.AnchorLine,
                    anchor.AnchorCharacter,
                    anchor.BindingStartLine,
                    anchor.BindingStartCharacter,
                    anchor.BindingEndLine,
                    anchor.BindingEndCharacter,
                };

                var hint = new JObject
                {
                    ["position"] = new JObject
                    {
                        ["line"] = anchor.BadgeLine,
                        ["character"] = anchor.BadgeCharacter,
                    },
                    ["label"] = new JArray
                    {
                        new JObject
                        {
                            ["value"] = "SQL Preview",
                            ["tooltip"] = "Open SQL preview",
                            ["command"] = new JObject
                            {
                                ["title"] = "Open SQL Preview",
                                ["command"] = "efquerylens.showSql",
                                ["arguments"] = commandArgs,
                            },
                        },
                    },
                    ["paddingLeft"] = true,
                    ["paddingRight"] = true,
                    ["tooltip"] = "Click to view query SQL",
                    ["data"] = new JObject
                    {
                        ["uri"] = uriText,
                        ["line"] = anchor.AnchorLine,
                        ["character"] = anchor.AnchorCharacter,
                    }
                };

                hints.Add(hint);
            }

            var result = hints.ToArray();
            _hintCache[filePath] = new CachedInlayHintResult(sourceHash, nowTicks, result);
            LogDebug($"handle-success count={result.Length}");
            return result;
        }
        catch (Exception ex)
        {
            LogDebug($"handle-exception type={ex.GetType().Name} message={ex.Message}");
            return [];
        }
    }

    public Task<JObject> ResolveAsync(JObject request, CancellationToken cancellationToken)
    {
        return Task.FromResult(request);
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

    private void LogDebug(string message)
    {
        if (_debugEnabled)
        {
            Console.Error.WriteLine($"[QL-InlayHint] {message}");
        }
    }

    private sealed record CachedInlayHintResult(int SourceHash, long CreatedAtTicks, JObject[] Hints);

    private static int ReadIntEnvironmentVariable(string variableName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(variableName);
        if (!int.TryParse(raw, out var value))
        {
            return fallback;
        }

        if (value < min) return min;
        if (value > max) return max;
        return value;
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

        return raw.Equals("1", StringComparison.OrdinalIgnoreCase);
    }
}
