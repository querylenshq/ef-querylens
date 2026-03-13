using System.Collections.Concurrent;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using EFQueryLens.Lsp.Parsing;
using EFQueryLens.Lsp.Services;
using Range = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace EFQueryLens.Lsp.Handlers;

internal sealed class CodeLensHandler
{
    private const int DefaultMaxCodeLensPerDocument = 50;
    private const int DefaultDebounceMilliseconds = 250;

    private readonly DocumentManager _documentManager;
    private readonly CodeLensPreviewService _codeLensPreviewService;
    private readonly int _maxCodeLensPerDocument;
    private readonly int _debounceMilliseconds;
    private readonly bool _debugEnabled;
    private readonly bool _forceCodeLens;
    private readonly ConcurrentDictionary<string, CachedCodeLensResult> _codeLensCache =
        new(StringComparer.OrdinalIgnoreCase);

    public CodeLensHandler(DocumentManager documentManager, CodeLensPreviewService codeLensPreviewService)
    {
        _documentManager = documentManager;
        _codeLensPreviewService = codeLensPreviewService;
        _maxCodeLensPerDocument = ReadIntEnvironmentVariable(
            "QUERYLENS_MAX_CODELENS_PER_DOCUMENT",
            DefaultMaxCodeLensPerDocument,
            min: 1,
            max: 500);
        _debounceMilliseconds = ReadIntEnvironmentVariable(
            "QUERYLENS_CODELENS_DEBOUNCE_MS",
            DefaultDebounceMilliseconds,
            min: 0,
            max: 5000);
        _debugEnabled = ReadBoolEnvironmentVariable("QUERYLENS_DEBUG", fallback: false);
        _forceCodeLens = ReadBoolEnvironmentVariable("QUERYLENS_FORCE_CODELENS", fallback: false);

        LogDebug($"initialized max={_maxCodeLensPerDocument} debounceMs={_debounceMilliseconds} forceCodeLens={_forceCodeLens}");
    }

    public async Task<CodeLens[]?> HandleAsync(CodeLensParams request, CancellationToken cancellationToken)
    {
        try
        {
            var uriText = request.TextDocument.Uri.ToString();
            var filePath = DocumentPathResolver.Resolve(request.TextDocument.Uri);
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
                && _codeLensCache.TryGetValue(filePath, out var cachedLensResult)
                && cachedLensResult.SourceHash == sourceHash
                && cachedLensResult.CreatedAtTicks + TimeSpan.FromMilliseconds(_debounceMilliseconds).Ticks > nowTicks)
            {
                LogDebug($"handle-exit reason=cache-hit count={cachedLensResult.Lenses.Length}");
                return cachedLensResult.Lenses;
            }

            var anchors = _codeLensPreviewService.ComputeAnchors(filePath, sourceText, _maxCodeLensPerDocument);
            if (anchors.Count == 0)
            {
                LogDebug("handle-exit reason=no-anchors");
                return [];
            }

            var lenses = new List<CodeLens>();
            foreach (var anchor in anchors)
            {
                // Range = badge position (above the query); use a 1-char range at line start so clients place the badge at the start of the line.
                var lens = new CodeLens
                {
                    Range = new Range
                    {
                        Start = new Position(anchor.BadgeLine, anchor.BadgeCharacter),
                        End = new Position(anchor.BadgeLine, Math.Max(anchor.BadgeCharacter + 1, 1)),
                    },
                    Command = new Command
                    {
                        CommandIdentifier = "efquerylens.showSql",
                        Title = "SQL Preview",
                        Arguments = new object[]
                        {
                            uriText,
                            anchor.AnchorLine,
                            anchor.AnchorCharacter,
                            anchor.BindingStartLine,
                            anchor.BindingStartCharacter,
                            anchor.BindingEndLine,
                            anchor.BindingEndCharacter,
                        },
                    },
                };

                LogDebug($"anchor badge=L{anchor.BadgeLine}:C{anchor.BadgeCharacter} anchor=L{anchor.AnchorLine}:C{anchor.AnchorCharacter} binding=L{anchor.BindingStartLine}:C{anchor.BindingStartCharacter}..L{anchor.BindingEndLine}:C{anchor.BindingEndCharacter}");
                lenses.Add(lens);
            }

            var result = lenses.ToArray();
            _codeLensCache[filePath] = new CachedCodeLensResult(sourceHash, nowTicks, result);
            LogDebug($"handle-success count={result.Length}");
            return result;
        }
        catch (Exception ex)
        {
            LogDebug($"handle-exception type={ex.GetType().Name} message={ex.Message}");
            return [];
        }
    }

    public Task<CodeLens> ResolveAsync(CodeLens request, CancellationToken cancellationToken)
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

        Console.Error.WriteLine($"[QL-CodeLens] {message}");
    }

    private sealed record CachedCodeLensResult(int SourceHash, long CreatedAtTicks, CodeLens[] Lenses);
}
