using EFQueryLens.Core;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Engine;
using EFQueryLens.Lsp.Parsing;
using System.Diagnostics;
using EFQueryLens.Core.Contracts;

namespace EFQueryLens.Lsp.Services;

internal sealed partial class HoverPreviewService
{
    private sealed record HoverCanonicalComputationResult(
        bool Success,
        string Message,
        QueryTranslationStatus Status,
        double AvgTranslationMs,
        double LastTranslationMs,
        string? SourceExpression,
        string? ExecutedExpression,
        int SourceLine,
        TranslationMetadata? Metadata,
        IReadOnlyList<QuerySqlCommand> Commands,
        IReadOnlyList<QueryWarning> Warnings);

    private static string BuildStatusText(QueryTranslationStatus status) => status switch
    {
        QueryTranslationStatus.Starting => "EF QueryLens - starting up",
        QueryTranslationStatus.InQueue => "EF QueryLens - in queue",
        QueryTranslationStatus.DaemonUnavailable => "EF QueryLens - error",
        _ => "EF QueryLens - in queue",
    };

    private async Task<HoverCanonicalComputationResult> BuildCanonicalAsync(
        string filePath,
        string sourceText,
        int line,
        int character,
        CancellationToken cancellationToken,
        Action<string> log,
        string? preresolvedExpression = null,
        string? preresolvedContextVariable = null)
    {
        static HoverCanonicalComputationResult Fail(
            string message,
            int sourceLine,
            QueryTranslationStatus status = QueryTranslationStatus.Ready) =>
            new(
                Success: false,
                Message: message,
                Status: status,
                AvgTranslationMs: 0,
                LastTranslationMs: 0,
                SourceExpression: null,
                ExecutedExpression: null,
                SourceLine: sourceLine,
                Metadata: null,
                Commands: [],
                Warnings: []);

        var sourceLine = line + 1;

        string? expression;
        string? contextVariableName;
        if (!string.IsNullOrWhiteSpace(preresolvedExpression)
            && !string.IsNullOrWhiteSpace(preresolvedContextVariable))
        {
            expression = preresolvedExpression;
            contextVariableName = preresolvedContextVariable;
            log($"extract-linq line={line} char={character} found=True ctx={contextVariableName} source=preresolved");
        }
        else
        {
            expression = LspSyntaxHelper.TryExtractLinqExpression(
                sourceText,
                line,
                character,
                out contextVariableName,
                sourceFilePath: filePath);
            log($"extract-linq line={line} char={character} found={!string.IsNullOrWhiteSpace(expression)} ctx={contextVariableName}");
        }

        if (string.IsNullOrWhiteSpace(expression) || string.IsNullOrWhiteSpace(contextVariableName))
        {
            return Fail(
                "Could not extract a LINQ query expression at the current caret location.",
                sourceLine,
                QueryTranslationStatus.Ready);
        }

        var targetAssembly = AssemblyResolver.TryGetTargetAssembly(filePath);
        if (string.IsNullOrWhiteSpace(targetAssembly)
            || targetAssembly.StartsWith("DEBUG_FAIL", StringComparison.Ordinal)
            || !File.Exists(targetAssembly))
        {
            return Fail(AssemblyResolver.FormatTargetAssemblyFailureMessage(targetAssembly), sourceLine);
        }

        var translationRequest = TranslationRequestBuilder.TryBuild(
            filePath,
            sourceText,
            expression,
            contextVariableName,
            line,
            character);
        if (translationRequest is null)
        {
            return Fail("Could not build a translation request for the current caret location.", sourceLine);
        }

        log($"extract-local-types line={line} char={character} count={translationRequest.LocalVariableTypes.Count} vars={string.Join(",", translationRequest.LocalVariableTypes.Keys)}");

        try
        {
            var sw = Stopwatch.StartNew();
            log($"translate-start line={line} char={character} assembly={targetAssembly}");

            var queued = await TranslateQueuedOrImmediateAsync(translationRequest, cancellationToken);

            if (queued.Status is not QueryTranslationStatus.Ready)
            {
                sw.Stop();
                var statusMessage = BuildStatusText(queued.Status);
                log(
                    $"queued-status line={line} char={character} " +
                    $"status={queued.Status} avgMs={queued.AverageTranslationMs:0.##} lastMs={queued.LastTranslationMs:0.##}");

                return new HoverCanonicalComputationResult(
                    Success: true,
                    Message: statusMessage,
                    Status: queued.Status,
                    AvgTranslationMs: queued.AverageTranslationMs,
                    LastTranslationMs: queued.LastTranslationMs,
                    SourceExpression: expression,
                    ExecutedExpression: null,
                    SourceLine: sourceLine,
                    Metadata: null,
                    Commands: [],
                    Warnings: []);
            }

            var translation = queued.Result;
            if (translation is null)
            {
                sw.Stop();
                log($"translate-missing-result line={line} char={character}");
                return Fail("Queued translation completed without a result payload.", sourceLine);
            }

            sw.Stop();
            QueryLensOperationalLog.Info(
                $"translate-ready file={Path.GetFileName(filePath)} line={line} char={character} " +
                $"elapsedMs={sw.ElapsedMilliseconds} commands={translation.Commands?.Count ?? 0}");
            log(
                $"translate-finished line={line} char={character} " +
                $"success={translation.Success} elapsedMs={sw.ElapsedMilliseconds} " +
                $"commands={translation.Commands.Count} sqlLen={(translation.Sql?.Length ?? 0)}");

            if (!translation.Success)
            {
                log($"translate-error line={line} char={character} message={translation.ErrorMessage}");
                return Fail(translation.ErrorMessage ?? "Translation failed.", sourceLine);
            }

            var commands = translation.Commands.Count > 0
                ? translation.Commands
                : translation.Sql is null
                    ? []
                    : [new QuerySqlCommand { Sql = translation.Sql, Parameters = translation.Parameters }];

            if (commands.Count == 0)
            {
                log($"translate-empty-commands line={line} char={character}");
                return Fail("No SQL was produced for this expression.", sourceLine);
            }

            return new HoverCanonicalComputationResult(
                Success: true,
                Message: string.Empty,
                Status: QueryTranslationStatus.Ready,
                AvgTranslationMs: queued.AverageTranslationMs,
                LastTranslationMs: queued.LastTranslationMs,
                SourceExpression: expression,
                ExecutedExpression: translation.ExecutedExpression,
                SourceLine: sourceLine,
                Metadata: translation.Metadata,
                Commands: commands,
                Warnings: translation.Warnings);
        }
        catch (Exception ex)
        {
            log($"translate-exception line={line} char={character} type={ex.GetType().Name} message={ex.Message}");
            return Fail($"{ex.GetType().Name}: {ex.Message}", sourceLine, QueryTranslationStatus.DaemonUnavailable);
        }
    }

    /// <summary>
    /// Fire-and-forget daemon translation warm for a LINQ position. Populates the daemon
    /// cache without blocking on hover formatting.
    /// </summary>
    internal async Task WarmAtPositionAsync(
        string filePath,
        string sourceText,
        int line,
        int character,
        CancellationToken cancellationToken)
    {
        if (_engine is not IEngineControl control)
        {
            return;
        }

        var expression = LspSyntaxHelper.TryExtractLinqExpression(
            sourceText,
            line,
            character,
            out var contextVariableName,
            sourceFilePath: filePath);
        if (string.IsNullOrWhiteSpace(expression) || string.IsNullOrWhiteSpace(contextVariableName))
        {
            return;
        }

        var request = TranslationRequestBuilder.TryBuild(
            filePath,
            sourceText,
            expression,
            contextVariableName,
            line,
            character);
        if (request is null || string.IsNullOrWhiteSpace(request.AssemblyPath))
        {
            return;
        }

        try
        {
            await control.WarmTranslateAsync(request, cancellationToken);
        }
        catch
        {
            // Best-effort — prewarm must never surface errors to the LSP host.
        }
    }

    internal async Task<CombinedHoverResult> BuildCombinedAsync(
        string filePath,
        string sourceText,
        int line,
        int character,
        CancellationToken cancellationToken,
        string? preresolvedExpression = null,
        string? preresolvedContextVariable = null)
    {
        Action<string> log = message => LogDebug($"combined {message}");
        var canonical = await BuildCanonicalAsync(
            filePath,
            sourceText,
            line,
            character,
            cancellationToken,
            log,
            preresolvedExpression,
            preresolvedContextVariable);

        var markdown = FormatMarkdown(canonical, filePath, line, character);
        var structured = FormatStructured(canonical, filePath, line, character);

        if (markdown.Success && markdown.Status is QueryTranslationStatus.Ready)
        {
            LogDebug($"combined hover-ready line={line} char={character} markdownLen={markdown.Output.Length}");
        }

        return new CombinedHoverResult(markdown, structured);
    }

    private async Task<QueuedTranslationResult> TranslateQueuedOrImmediateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _engine.TranslateAsync(request, cancellationToken);
        var lastTranslationMs = Math.Max(0, result.Metadata.TranslationTime.TotalMilliseconds);
        return new QueuedTranslationResult
        {
            Status = QueryTranslationStatus.Ready,
            AverageTranslationMs = 0,
            LastTranslationMs = lastTranslationMs,
            Result = result,
        };
    }
}