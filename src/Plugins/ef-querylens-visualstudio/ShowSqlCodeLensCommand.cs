using System.Collections;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.RpcContracts.OpenDocument;
using Microsoft.VisualStudio.RpcContracts.Utilities;

namespace EFQueryLens.VisualStudio;

[VisualStudioContribution]
internal sealed class ShowSqlCodeLensCommand : Command
{
    public override CommandConfiguration CommandConfiguration => new("%EFQueryLens.Command.ShowSql.DisplayName%");

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        QueryLensLogger.Info("show-sql-command-invoked");
        if (!TryExtractCommandLocation(context, out var targetUri, out var line, out var character))
        {
            QueryLensLogger.Info($"show-sql-command-parse-failed context={SummarizeContext(context)}");
            await this.Extensibility.Shell().ShowPromptAsync(
                "QueryLens could not resolve the target location for this CodeLens.",
                PromptOptions.OK,
                cancellationToken);
            return;
        }

        _ = TryExtractSqlPreviewData(context, out var sqlPreview, out var translationError, out var expression);
        if (!string.IsNullOrWhiteSpace(sqlPreview))
        {
            await OpenSqlPreviewDocumentAsync(targetUri, line, character, sqlPreview, expression, cancellationToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(translationError))
        {
            QueryLensLogger.Info($"show-sql-command-translation-error error={translationError}");
            await this.Extensibility.Shell().ShowPromptAsync(
                $"QueryLens could not generate SQL for this query.\n\n{translationError}",
                PromptOptions.OK,
                cancellationToken);
        }

        await OpenSourceLocationAsync(targetUri, line, character, cancellationToken);
    }

    private async Task OpenSourceLocationAsync(Uri targetUri, int line, int character, CancellationToken cancellationToken)
    {

        var clampedLine = Math.Max(0, line);
        var clampedCharacter = Math.Max(0, character);
        QueryLensLogger.Info($"show-sql-command-target uri={targetUri} line={clampedLine} character={clampedCharacter}");
        var caretRange = new Microsoft.VisualStudio.RpcContracts.Utilities.Range(
            clampedLine,
            clampedCharacter,
            clampedLine,
            clampedCharacter);
        var openOptions = new OpenDocumentOptions(
            selection: caretRange,
            ensureVisible: caretRange,
            ensureVisibleOptions: EnsureRangeVisibleOptions.AlwaysCenter,
            isPreview: false,
            activate: true,
            logicalView: LogicalViewKind.Text,
            projectId: null,
            editorType: null);

        try
        {
            await this.Extensibility.Documents().OpenDocumentAsync(targetUri, openOptions, cancellationToken);
            QueryLensLogger.Info("show-sql-command-open-document-success");
        }
        catch (Exception ex)
        {
            QueryLensLogger.Error("show-sql-command-open-document-failed", ex);
            throw;
        }
    }

    private async Task OpenSqlPreviewDocumentAsync(
        Uri sourceUri,
        int line,
        int character,
        string sqlPreview,
        string? expression,
        CancellationToken cancellationToken)
    {
        var previewDirectory = Path.Combine(Path.GetTempPath(), "EFQueryLens", "sql-previews");
        Directory.CreateDirectory(previewDirectory);

        var sourceName = Path.GetFileNameWithoutExtension(sourceUri.LocalPath);
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            sourceName = "query";
        }

        var previewFileName = $"{sourceName}-L{Math.Max(0, line) + 1}-C{Math.Max(0, character) + 1}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.sql";
        var previewPath = Path.Combine(previewDirectory, previewFileName);

        var content = BuildSqlPreviewContent(sourceUri, line, character, expression, sqlPreview);
        await File.WriteAllTextAsync(previewPath, content, Encoding.UTF8, cancellationToken);

        var previewUri = new Uri(previewPath);
        var topRange = new Microsoft.VisualStudio.RpcContracts.Utilities.Range(0, 0, 0, 0);
        var openOptions = new OpenDocumentOptions(
            selection: topRange,
            ensureVisible: topRange,
            ensureVisibleOptions: EnsureRangeVisibleOptions.AlwaysCenter,
            isPreview: false,
            activate: true,
            logicalView: LogicalViewKind.Text,
            projectId: null,
            editorType: null);

        await this.Extensibility.Documents().OpenDocumentAsync(previewUri, openOptions, cancellationToken);
        QueryLensLogger.Info($"show-sql-command-open-sql-preview path={previewPath}");
    }

    private static string BuildSqlPreviewContent(
        Uri sourceUri,
        int line,
        int character,
        string? expression,
        string sqlPreview)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-- QueryLens SQL Preview")
            .Append("-- Generated UTC: ")
            .AppendLine(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append("-- Source: ")
            .AppendLine(sourceUri.LocalPath)
            .Append("-- Location: line ")
            .Append(line + 1)
            .Append(", column ")
            .AppendLine((character + 1).ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(expression))
        {
            builder.Append("-- Expression: ")
                .AppendLine(expression.Replace('\r', ' ').Replace('\n', ' ').Trim());
        }

        builder.AppendLine()
            .AppendLine(sqlPreview.Trim())
            .AppendLine();

        return builder.ToString();
    }

    private static string SummarizeContext(IClientContext context)
    {
        if (context is not IClientContextPrivate privateContext)
        {
            return "no-private-context";
        }

        try
        {
            var dictionary = privateContext.AsDictionary();
            var keys = dictionary.Keys.Take(20);
            return $"keys=[{string.Join(",", keys)}] count={dictionary.Count}";
        }
        catch (Exception ex)
        {
            return $"context-summary-failed:{ex.GetType().Name}";
        }
    }

    private static bool TryExtractCommandLocation(
        IClientContext context,
        out Uri targetUri,
        out int line,
        out int character)
    {
        targetUri = null!;
        line = 0;
        character = 0;

        if (context is not IClientContextPrivate privateContext)
        {
            return false;
        }

        return TryExtractFromValue(privateContext.AsDictionary(), out targetUri, out line, out character, depth: 0);
    }

    internal static bool TryExtractSqlPreviewData(
        IClientContext context,
        out string sqlPreview,
        out string translationError,
        out string expression)
    {
        sqlPreview = string.Empty;
        translationError = string.Empty;
        expression = string.Empty;

        if (context is not IClientContextPrivate privateContext)
        {
            return false;
        }

        return TryExtractSqlPreviewFromValue(
            privateContext.AsDictionary(),
            out sqlPreview,
            out translationError,
            out expression,
            depth: 0);
    }

    private static bool TryExtractSqlPreviewFromValue(
        object? value,
        out string sqlPreview,
        out string translationError,
        out string expression,
        int depth)
    {
        sqlPreview = string.Empty;
        translationError = string.Empty;
        expression = string.Empty;

        if (value is null || depth > 6)
        {
            return false;
        }

        if (TryExtractSqlPreviewFromArray(value, out sqlPreview, out translationError, out expression))
        {
            return true;
        }

        if (value is string text && TryExtractSqlPreviewFromJsonArray(text, out sqlPreview, out translationError, out expression))
        {
            return true;
        }

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (TryExtractSqlPreviewFromValue(entry.Value, out sqlPreview, out translationError, out expression, depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
            {
                if (TryExtractSqlPreviewFromValue(item, out sqlPreview, out translationError, out expression, depth + 1))
                {
                    return true;
                }
            }
        }

        return TryExtractSqlPreviewFromProperties(value, out sqlPreview, out translationError, out expression);
    }

    private static bool TryExtractSqlPreviewFromArray(
        object value,
        out string sqlPreview,
        out string translationError,
        out string expression)
    {
        sqlPreview = string.Empty;
        translationError = string.Empty;
        expression = string.Empty;

        if (value is not IEnumerable enumerable || value is string)
        {
            return false;
        }

        var items = new List<object?>();
        foreach (var item in enumerable)
        {
            items.Add(item);
            if (items.Count == 6)
            {
                break;
            }
        }

        if (items.Count < 4)
        {
            return false;
        }

        if (!TryParseUri(items[0], out _) || !TryParseInt(items[1], out _) || !TryParseInt(items[2], out _))
        {
            return false;
        }

        sqlPreview = items[3]?.ToString() ?? string.Empty;
        if (items.Count > 4)
        {
            translationError = items[4]?.ToString() ?? string.Empty;
        }

        if (items.Count > 5)
        {
            expression = items[5]?.ToString() ?? string.Empty;
        }

        return true;
    }

    private static bool TryExtractSqlPreviewFromJsonArray(
        string text,
        out string sqlPreview,
        out string translationError,
        out string expression)
    {
        sqlPreview = string.Empty;
        translationError = string.Empty;
        expression = string.Empty;

        text = text.Trim();
        if (!text.StartsWith("[", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 4)
            {
                return false;
            }

            if (!TryParseUri(root[0].ToString(), out _) ||
                !int.TryParse(root[1].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
                !int.TryParse(root[2].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }

            sqlPreview = root[3].ToString();
            if (root.GetArrayLength() > 4)
            {
                translationError = root[4].ToString();
            }

            if (root.GetArrayLength() > 5)
            {
                expression = root[5].ToString();
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryExtractSqlPreviewFromProperties(
        object value,
        out string sqlPreview,
        out string translationError,
        out string expression)
    {
        sqlPreview = string.Empty;
        translationError = string.Empty;
        expression = string.Empty;

        var type = value.GetType();
        var uriProperty = type.GetProperty("Uri") ?? type.GetProperty("uri");
        var lineProperty = type.GetProperty("Line") ?? type.GetProperty("line");
        var characterProperty = type.GetProperty("Character") ?? type.GetProperty("character");
        if (uriProperty is null || lineProperty is null || characterProperty is null)
        {
            return false;
        }

        var uriValue = uriProperty.GetValue(value);
        var lineValue = lineProperty.GetValue(value);
        var characterValue = characterProperty.GetValue(value);
        if (!TryParseUri(uriValue, out _) || !TryParseInt(lineValue, out _) || !TryParseInt(characterValue, out _))
        {
            return false;
        }

        var sqlProperty = type.GetProperty("Sql") ?? type.GetProperty("sql");
        var errorProperty = type.GetProperty("Error") ?? type.GetProperty("error");
        var expressionProperty = type.GetProperty("Expression") ?? type.GetProperty("expression");
        sqlPreview = sqlProperty?.GetValue(value)?.ToString() ?? string.Empty;
        translationError = errorProperty?.GetValue(value)?.ToString() ?? string.Empty;
        expression = expressionProperty?.GetValue(value)?.ToString() ?? string.Empty;
        return true;
    }

    private static bool TryExtractFromValue(
        object? value,
        out Uri targetUri,
        out int line,
        out int character,
        int depth)
    {
        targetUri = null!;
        line = 0;
        character = 0;

        if (value is null || depth > 6)
        {
            return false;
        }

        if (TryExtractFromTriplet(value, out targetUri, out line, out character))
        {
            return true;
        }

        if (value is string text && TryExtractFromJsonArray(text, out targetUri, out line, out character))
        {
            return true;
        }

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (TryExtractFromValue(entry.Value, out targetUri, out line, out character, depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
            {
                if (TryExtractFromValue(item, out targetUri, out line, out character, depth + 1))
                {
                    return true;
                }
            }
        }

        return TryExtractFromProperties(value, out targetUri, out line, out character);
    }

    private static bool TryExtractFromTriplet(
        object value,
        out Uri targetUri,
        out int line,
        out int character)
    {
        targetUri = null!;
        line = 0;
        character = 0;

        if (value is not IEnumerable enumerable || value is string)
        {
            return false;
        }

        var items = new List<object?>();
        foreach (var item in enumerable)
        {
            items.Add(item);
            if (items.Count == 3)
            {
                break;
            }
        }

        if (items.Count != 3)
        {
            return false;
        }

        if (!TryParseUri(items[0], out targetUri))
        {
            return false;
        }

        if (!TryParseInt(items[1], out line) || !TryParseInt(items[2], out character))
        {
            return false;
        }

        return true;
    }

    private static bool TryExtractFromJsonArray(
        string text,
        out Uri targetUri,
        out int line,
        out int character)
    {
        targetUri = null!;
        line = 0;
        character = 0;

        text = text.Trim();
        if (!text.StartsWith("[", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 3)
            {
                return false;
            }

            var uriToken = root[0].ToString();
            var lineToken = root[1].ToString();
            var charToken = root[2].ToString();

            if (!TryParseUri(uriToken, out targetUri))
            {
                return false;
            }

            if (!int.TryParse(lineToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out line))
            {
                return false;
            }

            return int.TryParse(charToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out character);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryExtractFromProperties(
        object value,
        out Uri targetUri,
        out int line,
        out int character)
    {
        targetUri = null!;
        line = 0;
        character = 0;

        var type = value.GetType();
        var uriProperty = type.GetProperty("Uri") ?? type.GetProperty("uri");
        var lineProperty = type.GetProperty("Line") ?? type.GetProperty("line");
        var characterProperty = type.GetProperty("Character") ?? type.GetProperty("character");

        if (uriProperty is null || lineProperty is null || characterProperty is null)
        {
            return false;
        }

        var uriValue = uriProperty.GetValue(value);
        var lineValue = lineProperty.GetValue(value);
        var characterValue = characterProperty.GetValue(value);

        if (!TryParseUri(uriValue, out targetUri))
        {
            return false;
        }

        if (!TryParseInt(lineValue, out line) || !TryParseInt(characterValue, out character))
        {
            return false;
        }

        return true;
    }

    private static bool TryParseUri(object? value, out Uri targetUri)
    {
        targetUri = null!;

        var uriText = value switch
        {
            Uri uri => uri.ToString(),
            _ => value?.ToString(),
        };

        if (string.IsNullOrWhiteSpace(uriText))
        {
            return false;
        }

        if (Uri.TryCreate(uriText, UriKind.Absolute, out var parsedAbsolute))
        {
            targetUri = parsedAbsolute;
            return true;
        }

        if (Path.IsPathRooted(uriText))
        {
            targetUri = new Uri(uriText);
            return true;
        }

        return false;
    }

    private static bool TryParseInt(object? value, out int result)
    {
        result = 0;
        if (value is null)
        {
            return false;
        }

        if (value is int intValue)
        {
            result = intValue;
            return true;
        }

        if (value is long longValue && longValue >= int.MinValue && longValue <= int.MaxValue)
        {
            result = (int)longValue;
            return true;
        }

        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }
}
