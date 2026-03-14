using System.Collections.Concurrent;
using System.Diagnostics;
using EFQueryLens.Core;
using EFQueryLens.Lsp.Parsing;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Lsp.Handlers;

internal sealed record WarmupResponse(bool Success, bool Cached, string? AssemblyPath, string? Message);

internal sealed class WarmupHandler
{
    private readonly DocumentManager _documentManager;
    private readonly IQueryLensEngine _engine;
    private readonly bool _debugEnabled;
    private readonly int _successTtlMs;
    private readonly int _failureCooldownMs;
    private readonly ConcurrentDictionary<string, CachedWarmup> _warmCache =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed record CachedWarmup(long ExpiresAtUtcTicks, bool Success, string Message);

    public WarmupHandler(DocumentManager documentManager, IQueryLensEngine engine)
    {
        _documentManager = documentManager;
        _engine = engine;
        _debugEnabled = ReadBoolEnvironmentVariable("QUERYLENS_DEBUG", fallback: false);
        _successTtlMs = ReadIntEnvironmentVariable(
            "QUERYLENS_WARMUP_SUCCESS_TTL_MS",
            fallback: 60_000,
            min: 0,
            max: 600_000);
        _failureCooldownMs = ReadIntEnvironmentVariable(
            "QUERYLENS_WARMUP_FAILURE_COOLDOWN_MS",
            fallback: 5_000,
            min: 0,
            max: 120_000);
    }

    public async Task<WarmupResponse> HandleAsync(TextDocumentPositionParams request, CancellationToken cancellationToken)
    {
        var filePath = DocumentPathResolver.Resolve(request.TextDocument.Uri);
        var documentUri = request.TextDocument.Uri.ToString();

        var sourceText = await GetSourceTextAsync(documentUri, filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return new WarmupResponse(false, false, null, "empty-source");
        }

        if (LspSyntaxHelper.FindAllLinqChains(sourceText).Count == 0)
        {
            return new WarmupResponse(false, false, null, "no-linq-chain");
        }

        var targetAssembly = AssemblyResolver.TryGetTargetAssembly(filePath);
        if (string.IsNullOrWhiteSpace(targetAssembly)
            || targetAssembly.StartsWith("DEBUG_FAIL", StringComparison.Ordinal)
            || !File.Exists(targetAssembly))
        {
            return new WarmupResponse(false, false, targetAssembly, "assembly-not-found");
        }

        if (TryGetCachedWarmup(targetAssembly, out var cached))
        {
            LogDebug($"warmup-cache-hit assembly={targetAssembly} success={cached.Success} message={cached.Message}");
            return new WarmupResponse(cached.Success, true, targetAssembly, cached.Message);
        }

        var dbContextTypeName = TryResolveDbContextTypeName(
            sourceText,
            request.Position.Line,
            request.Position.Character);

        var sw = Stopwatch.StartNew();
        try
        {
            await _engine.InspectModelAsync(new ModelInspectionRequest
            {
                AssemblyPath = targetAssembly,
                DbContextTypeName = dbContextTypeName,
            }, cancellationToken);

            sw.Stop();
            CacheWarmup(targetAssembly, success: true, "ready");
            LogDebug($"warmup-success assembly={targetAssembly} elapsedMs={sw.ElapsedMilliseconds} context={dbContextTypeName ?? "<auto>"}");
            return new WarmupResponse(true, false, targetAssembly, "ready");
        }
        catch (Exception ex)
        {
            sw.Stop();

            // Warmup is best-effort; when multiple DbContexts exist and no explicit
            // context can be inferred, avoid surfacing this as a hard warmup failure.
            if (IsMultipleDbContextAmbiguity(ex))
            {
                CacheWarmup(targetAssembly, success: true, "skipped-multi-dbcontext");
                LogDebug($"warmup-skipped assembly={targetAssembly} elapsedMs={sw.ElapsedMilliseconds} reason=multi-dbcontext context={dbContextTypeName ?? "<auto>"}");
                return new WarmupResponse(true, false, targetAssembly, "skipped-multi-dbcontext");
            }

            CacheWarmup(targetAssembly, success: false, ex.GetType().Name);
            LogDebug($"warmup-failed assembly={targetAssembly} elapsedMs={sw.ElapsedMilliseconds} type={ex.GetType().Name} message={ex.Message} context={dbContextTypeName ?? "<auto>"}");
            return new WarmupResponse(false, false, targetAssembly, ex.GetType().Name);
        }
    }

    private static bool IsMultipleDbContextAmbiguity(Exception ex)
    {
        return ex.Message.Contains("Multiple DbContext types found", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryResolveDbContextTypeName(string sourceText, int line, int character)
    {
        _ = LspSyntaxHelper.TryExtractLinqExpression(sourceText, line, character, out var contextVariableName);
        if (string.IsNullOrWhiteSpace(contextVariableName))
        {
            return null;
        }

        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetRoot();

        // Prefer explicit fields/locals/parameters named as the context variable.
        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            if (field.Declaration.Variables.Any(v => v.Identifier.ValueText.Equals(contextVariableName, StringComparison.Ordinal)))
            {
                return field.Declaration.Type.ToString();
            }
        }

        foreach (var local in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            if (local.Declaration.Variables.Any(v => v.Identifier.ValueText.Equals(contextVariableName, StringComparison.Ordinal)))
            {
                return local.Declaration.Type.ToString();
            }
        }

        foreach (var parameter in root.DescendantNodes().OfType<ParameterSyntax>())
        {
            if (parameter.Identifier.ValueText.Equals(contextVariableName, StringComparison.Ordinal)
                && parameter.Type is not null)
            {
                return parameter.Type.ToString();
            }
        }

        return null;
    }

    private bool TryGetCachedWarmup(string assemblyPath, out CachedWarmup cached)
    {
        cached = default!;
        if (!_warmCache.TryGetValue(assemblyPath, out var existing))
        {
            return false;
        }

        if (existing.ExpiresAtUtcTicks <= DateTime.UtcNow.Ticks)
        {
            _warmCache.TryRemove(assemblyPath, out _);
            return false;
        }

        cached = existing;
        return true;
    }

    private void CacheWarmup(string assemblyPath, bool success, string message)
    {
        var ttlMs = success ? _successTtlMs : _failureCooldownMs;
        if (ttlMs <= 0)
        {
            _warmCache.TryRemove(assemblyPath, out _);
            return;
        }

        var expires = DateTime.UtcNow.AddMilliseconds(ttlMs).Ticks;
        _warmCache[assemblyPath] = new CachedWarmup(expires, success, message);
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

        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

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

        Console.Error.WriteLine($"[QL-Warmup] {message}");
    }
}
