using System.Reflection;
using EFQueryLens.Core;
using EFQueryLens.Core.Contracts;

namespace EFQueryLens.Lsp.Services;

internal sealed record CombinedHoverResult(
    HoverPreviewComputationResult Markdown,
    QueryLensStructuredHoverResult Structured);

internal sealed record HoverPreviewComputationResult(
    bool Success,
    string Output,
    QueryTranslationStatus Status = QueryTranslationStatus.Ready,
    double AvgTranslationMs = 0,
    double LastTranslationMs = 0);

internal sealed record QueryLensSqlStatement(string Sql, string? SplitLabel);

internal sealed record QueryLensStructuredHoverResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<QueryLensSqlStatement> Statements,
    int CommandCount,
    string? SourceExpression,
    string? ExecutedExpression,
    string? DbContextType,
    string? ProviderName,
    string? SourceFile,
    int SourceLine,
    IReadOnlyList<string> Warnings,
    string? EnrichedSql,
    string? Mode,
    QueryTranslationStatus Status = QueryTranslationStatus.Ready,
    string? StatusMessage = null,
    double AvgTranslationMs = 0,
    double LastTranslationMs = 0);

internal sealed partial class HoverPreviewService
{
    private static readonly string HoverBuildMarker = ResolveBuildMarker();

    private readonly IQueryLensEngine _engine;
    private bool _debugEnabled;

    public HoverPreviewService(IQueryLensEngine engine, bool debugEnabled = false)
    {
        _engine = engine;
        _debugEnabled = debugEnabled;

        Console.Error.WriteLine(
            $"[QL-Hover] init: client='{Environment.GetEnvironmentVariable("QUERYLENS_CLIENT")}' " +
            $"build={HoverBuildMarker}");
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

    private static string ResolveBuildMarker()
    {
        try
        {
            var assembly = typeof(HoverPreviewService).Assembly;
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                return informational;
            }

            var version = assembly.GetName().Version;
            if (version is not null)
            {
                return version.ToString();
            }
        }
        catch
        {
            // Fall through to sentinel.
        }

        return "unknown";
    }
}
