using EFQueryLens.Core;

namespace EFQueryLens.Lsp.Services;

internal sealed record HoverPreviewComputationResult(
    bool Success,
    string Output,
    QueryTranslationStatus Status = QueryTranslationStatus.Ready,
    double AvgTranslationMs = 0);

internal sealed record QueryLensSqlStatement(string Sql, string? SplitLabel);

internal sealed record QueryLensStructuredHoverResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<QueryLensSqlStatement> Statements,
    int CommandCount,
    string? SourceExpression,
    string? DbContextType,
    string? ProviderName,
    string? SourceFile,
    int SourceLine,
    IReadOnlyList<string> Warnings,
    string? EnrichedSql,
    string? Mode,
    QueryTranslationStatus Status = QueryTranslationStatus.Ready,
    string? StatusMessage = null,
    double AvgTranslationMs = 0);

internal sealed partial class HoverPreviewService
{
    private readonly IQueryLensEngine _engine;
    private bool _debugEnabled;

    public HoverPreviewService(IQueryLensEngine engine, bool debugEnabled = false)
    {
        _engine = engine;
        _debugEnabled = debugEnabled;
    }

    internal void SetDebugEnabled(bool enabled)
    {
        _debugEnabled = enabled;
    }

    private void LogDebug(string message)
    {
        if (!_debugEnabled)
        {
            return;
        }

        Console.Error.WriteLine($"[QL-Hover] {message}");
    }
}
