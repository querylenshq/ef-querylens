using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Globalization;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace EFQueryLens.VisualStudio;

[Export(typeof(ISuggestedActionsSourceProvider))]
[Name("EF QueryLens Light Bulb")]
[ContentType("CSharp")]
internal sealed class QueryLensLightBulbProvider : ISuggestedActionsSourceProvider
{
    public ISuggestedActionsSource CreateSuggestedActionsSource(ITextView textView, ITextBuffer textBuffer)
    {
        if (textView is null || textBuffer is null)
        {
            return null!;
        }

        return new QueryLensLightBulbSource(textView, textBuffer);
    }
}

internal sealed class QueryLensLightBulbSource : ISuggestedActionsSource
{
    private readonly ITextView _textView;
    private readonly ITextBuffer _textBuffer;
    private readonly object _lastResultGate = new();

    private SqlPreviewContext? _lastContext;
    private string? _lastSql;

    public QueryLensLightBulbSource(ITextView textView, ITextBuffer textBuffer)
    {
        _textView = textView;
        _textBuffer = textBuffer;
    }

#pragma warning disable CS0067
    public event EventHandler<EventArgs>? SuggestedActionsChanged;
#pragma warning restore CS0067

    public void Dispose()
    {
    }

    public bool TryGetTelemetryId(out Guid telemetryId)
    {
        telemetryId = Guid.Empty;
        return false;
    }

    public async Task<bool> HasSuggestedActionsAsync(
        ISuggestedActionCategorySet requestedActionCategories,
        SnapshotSpan range,
        CancellationToken cancellationToken)
    {
        var context = TryGetCurrentContext();
        if (context is null)
        {
            CacheLastResult(null, null);
            return false;
        }

        if (QueryLensLanguageClient.IsQueryLineKnown(context.FilePath, context.Line))
        {
            CacheLastResult(context, null);
            return true;
        }

        var sql = await QueryLensLanguageClient.TryGetSqlPreviewAsync(
            context.FilePath,
            context.Line,
            context.Character,
            cancellationToken);

        CacheLastResult(context, sql);
        return !string.IsNullOrWhiteSpace(sql);
    }

    public IEnumerable<SuggestedActionSet> GetSuggestedActions(
        ISuggestedActionCategorySet requestedActionCategories,
        SnapshotSpan range,
        CancellationToken cancellationToken)
    {
        var context = TryGetCurrentContext();
        if (context is null)
        {
            return Enumerable.Empty<SuggestedActionSet>();
        }

        var sql = GetCachedSql(context)
            ?? ThreadHelper.JoinableTaskFactory.Run(() => QueryLensLanguageClient.TryGetSqlPreviewAsync(
                context.FilePath,
                context.Line,
                context.Character,
                cancellationToken));

        if (string.IsNullOrWhiteSpace(sql))
        {
            return Enumerable.Empty<SuggestedActionSet>();
        }

        var formattedSql = SqlFormattingService.Format(sql!);

        var actions = new ISuggestedAction[]
        {
            new ShowSqlSuggestedAction(context, formattedSql),
            new CopySqlSuggestedAction(formattedSql),
        };

        return [new SuggestedActionSet(actions)];
    }

    private void CacheLastResult(SqlPreviewContext? context, string? sql)
    {
        lock (_lastResultGate)
        {
            _lastContext = context;
            _lastSql = sql;
        }
    }

    private string? GetCachedSql(SqlPreviewContext context)
    {
        lock (_lastResultGate)
        {
            if (_lastContext is null)
            {
                return null;
            }

            if (_lastContext.Line != context.Line || _lastContext.Character != context.Character)
            {
                return null;
            }

            if (!string.Equals(_lastContext.FilePath, context.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return _lastSql;
        }
    }

    private SqlPreviewContext? TryGetCurrentContext()
    {
        if (!_textBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(document.FilePath)
            || !document.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var caretPoint = _textView.Caret.Position.BufferPosition;
        var sourceSnapshot = _textBuffer.CurrentSnapshot;

        SnapshotPoint translatedPoint;
        try
        {
            translatedPoint = caretPoint.Snapshot == sourceSnapshot
                ? caretPoint
                : caretPoint.TranslateTo(sourceSnapshot, PointTrackingMode.Negative);
        }
        catch
        {
            return null;
        }

        var line = translatedPoint.GetContainingLine();
        var lineNumber = line.LineNumber;
        var character = Math.Max(0, translatedPoint.Position - line.Start.Position);

        return new SqlPreviewContext(document.FilePath, lineNumber, character);
    }
}

internal abstract class QueryLensSuggestedActionBase : ISuggestedAction
{
    public abstract string DisplayText { get; }

    public bool HasActionSets => false;

    public bool HasPreview => false;

    public ImageMoniker IconMoniker => default;

    public string? IconAutomationText => null;

    public string? InputGestureText => null;

    public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Enumerable.Empty<SuggestedActionSet>());
    }

    public Task<object?> GetPreviewAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<object?>(null);
    }

    public void Dispose()
    {
    }

    public bool TryGetTelemetryId(out Guid telemetryId)
    {
        telemetryId = Guid.Empty;
        return false;
    }

    public abstract void Invoke(CancellationToken cancellationToken);
}

internal sealed class ShowSqlSuggestedAction : QueryLensSuggestedActionBase
{
    private readonly SqlPreviewContext _context;
    private readonly string _sql;

    public ShowSqlSuggestedAction(SqlPreviewContext context, string sql)
    {
        _context = context;
        _sql = sql;
    }

    public override string DisplayText => "EF QueryLens: Show SQL Preview";

    public override void Invoke(CancellationToken cancellationToken)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                var previewPath = BuildPreviewFilePath(_context.FilePath, _context.Line, _context.Character);
                await Task.Run(
                    () => File.WriteAllText(previewPath, BuildSqlPreviewFile(_sql, _context.FilePath, _context.Line, _context.Character)),
                    cancellationToken);

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, previewPath);
            }
            catch (Exception ex)
            {
                QueryLensLogger.Error("show-sql-suggested-action-failed", ex);
            }
        });
    }

    private static string BuildPreviewFilePath(string sourcePath, int line, int character)
    {
        var directory = Path.Combine(Path.GetTempPath(), "EFQueryLens", "sql-previews");
        Directory.CreateDirectory(directory);

        var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            sourceName = "query";
        }

        var fileName = string.Format(
            CultureInfo.InvariantCulture,
            "{0}-L{1}-C{2}-{3:yyyyMMddHHmmssfff}.sql",
            sourceName,
            Math.Max(0, line) + 1,
            Math.Max(0, character) + 1,
            DateTime.UtcNow);

        return Path.Combine(directory, fileName);
    }

    private static string BuildSqlPreviewFile(string sql, string sourcePath, int line, int character)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "-- QueryLens SQL Preview{0}-- Generated UTC: {1:yyyy-MM-dd HH:mm:ss}{0}-- Source: {2}{0}-- Location: line {3}, column {4}{0}{0}{5}{0}",
            Environment.NewLine,
            DateTime.UtcNow,
            sourcePath,
            line + 1,
            character + 1,
            sql.Trim());
    }
}

internal sealed class CopySqlSuggestedAction : QueryLensSuggestedActionBase
{
    private readonly string _sql;

    public CopySqlSuggestedAction(string sql)
    {
        _sql = sql;
    }

    public override string DisplayText => "EF QueryLens: Copy SQL";

    public override void Invoke(CancellationToken cancellationToken)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "clip.exe",
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = new Process { StartInfo = startInfo };
                if (!process.Start())
                {
                    return;
                }

                await process.StandardInput.WriteAsync(_sql);
                await process.StandardInput.FlushAsync();
                process.StandardInput.Close();

                await Task.Run(() => process.WaitForExit(), cancellationToken);
            }
            catch (Exception ex)
            {
                QueryLensLogger.Error("copy-sql-suggested-action-failed", ex);
            }
        });
    }
}

internal sealed class SqlPreviewContext
{
    public SqlPreviewContext(string filePath, int line, int character)
    {
        FilePath = filePath;
        Line = line;
        Character = character;
    }

    public string FilePath { get; }

    public int Line { get; }

    public int Character { get; }
}
