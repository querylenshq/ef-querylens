using EFQueryLens.Core;
using EFQueryLens.Lsp.Parsing;
using SQL.Formatter;
using SQL.Formatter.Core;
using SQL.Formatter.Language;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EFQueryLens.Lsp.Services;

/// <summary>Embedded in each hover response as <!-- QUERYLENS_META:{base64} --> so all clients can build enriched copy/open content.</summary>
internal sealed record QueryLensHoverMetadataPayload
{
    [JsonPropertyName("SourceExpression")] public string SourceExpression { get; init; } = string.Empty;
    [JsonPropertyName("ExecutedExpression")] public string ExecutedExpression { get; init; } = string.Empty;
    [JsonPropertyName("Mode")] public string Mode { get; init; } = "direct";
    [JsonPropertyName("ModeDescription")] public string ModeDescription { get; init; } = string.Empty;
    [JsonPropertyName("Warnings")] public IReadOnlyList<string> Warnings { get; init; } = [];
    [JsonPropertyName("SourceFile")] public string SourceFile { get; init; } = string.Empty;
    [JsonPropertyName("SourceLine")] public int SourceLine { get; init; }
    [JsonPropertyName("DbContextType")] public string DbContextType { get; init; } = string.Empty;
    [JsonPropertyName("ProviderName")] public string ProviderName { get; init; } = string.Empty;
}

internal sealed record HoverPreviewComputationResult(bool Success, string Output);

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
    string? Mode);

internal sealed class HoverPreviewService
{
    private readonly IQueryLensEngine _engine;

    public HoverPreviewService(IQueryLensEngine engine)
    {
        _engine = engine;
    }

    public async Task<HoverPreviewComputationResult> BuildMarkdownAsync(
        string filePath,
        string sourceText,
        int line,
        int character,
        CancellationToken cancellationToken)
    {
        var expression = LspSyntaxHelper.TryExtractLinqExpression(sourceText, line, character, out var contextVariableName);
        Console.Error.WriteLine($"[QL-Hover] extract-linq line={line} char={character} found={!string.IsNullOrWhiteSpace(expression)} ctx={contextVariableName}");

        if (string.IsNullOrWhiteSpace(expression) || string.IsNullOrWhiteSpace(contextVariableName))
        {
            var fallback = LspSyntaxHelper.FindAllLinqChains(sourceText)
                .OrderBy(chain => Math.Abs(chain.Line - line))
                .ThenBy(chain => Math.Abs(chain.Character - character))
                .FirstOrDefault();

            if (fallback is not null)
            {
                Console.Error.WriteLine($"[QL-Hover] fallback-chain line={fallback.Line} char={fallback.Character} ctx={fallback.ContextVariableName}");
                expression = fallback.Expression;
                contextVariableName = fallback.ContextVariableName;
            }
        }

        if (string.IsNullOrWhiteSpace(expression) || string.IsNullOrWhiteSpace(contextVariableName))
        {
            return new HoverPreviewComputationResult(false, "Could not extract a LINQ query expression at the current caret location.");
        }

        var targetAssembly = AssemblyResolver.TryGetTargetAssembly(filePath);
        if (string.IsNullOrWhiteSpace(targetAssembly) ||
            targetAssembly.StartsWith("DEBUG_FAIL", StringComparison.Ordinal) ||
            !File.Exists(targetAssembly))
        {
            return new HoverPreviewComputationResult(false, "Could not locate compiled target assembly for this file. Build the project and try again.");
        }

        var usingContext = LspSyntaxHelper.ExtractUsingContext(sourceText);

        try
        {
            var sw = Stopwatch.StartNew();
            Console.Error.WriteLine($"[QL-Hover] translate-start line={line} char={character} assembly={targetAssembly}");

            var translation = await _engine.TranslateAsync(new TranslationRequest
            {
                AssemblyPath = targetAssembly,
                Expression = expression,
                ContextVariableName = contextVariableName,
                AdditionalImports = usingContext.Imports.ToArray(),
                UsingAliases = new Dictionary<string, string>(usingContext.Aliases, StringComparer.Ordinal),
                UsingStaticTypes = usingContext.StaticTypes.ToArray(),
            }, cancellationToken);

            sw.Stop();
            Console.Error.WriteLine($"[QL-Hover] translate-finished line={line} char={character} success={translation.Success} elapsedMs={sw.ElapsedMilliseconds} commands={translation.Commands.Count} sqlLen={(translation.Sql?.Length ?? 0)}");

            if (!translation.Success)
            {
                Console.Error.WriteLine($"[QL-Hover] translate-error line={line} char={character} message={translation.ErrorMessage}");
                return new HoverPreviewComputationResult(false, translation.ErrorMessage ?? "Translation failed.");
            }

            var commands = translation.Commands.Count > 0
                ? translation.Commands
                : translation.Sql is null
                    ? []
                    : [new QuerySqlCommand { Sql = translation.Sql, Parameters = translation.Parameters }];

            if (commands.Count == 0)
            {
                Console.Error.WriteLine($"[QL-Hover] translate-empty-commands line={line} char={character}");
                return new HoverPreviewComputationResult(false, "No SQL was produced for this expression.");
            }

            var documentUri = DocumentPathResolver.ToUri(filePath);
            var metadata = translation.Metadata;
            var markdown = BuildHoverMarkdown(
                commands, translation.Warnings, documentUri, line, character,
                expression ?? string.Empty, filePath, metadata);
            Console.Error.WriteLine($"[QL-Hover] hover-markdown-ready line={line} char={character} markdownLen={markdown.Length}");
            return new HoverPreviewComputationResult(true, markdown);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[QL-Hover] translate-exception line={line} char={character} type={ex.GetType().Name} message={ex.Message}");
            return new HoverPreviewComputationResult(false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public async Task<QueryLensStructuredHoverResult> BuildStructuredAsync(
        string filePath,
        string sourceText,
        int line,
        int character,
        CancellationToken cancellationToken)
    {
        static QueryLensStructuredHoverResult Fail(string msg) =>
            new(false, msg, [], 0, null, null, null, null, 0, [], null);

        var expression = LspSyntaxHelper.TryExtractLinqExpression(sourceText, line, character, out var contextVariableName);
        Console.Error.WriteLine($"[QL-Hover] structured extract-linq line={line} char={character} found={!string.IsNullOrWhiteSpace(expression)} ctx={contextVariableName}");

        if (string.IsNullOrWhiteSpace(expression) || string.IsNullOrWhiteSpace(contextVariableName))
        {
            var fallback = LspSyntaxHelper.FindAllLinqChains(sourceText)
                .OrderBy(chain => Math.Abs(chain.Line - line))
                .ThenBy(chain => Math.Abs(chain.Character - character))
                .FirstOrDefault();

            if (fallback is not null)
            {
                Console.Error.WriteLine($"[QL-Hover] structured fallback-chain line={fallback.Line} char={fallback.Character} ctx={fallback.ContextVariableName}");
                expression = fallback.Expression;
                contextVariableName = fallback.ContextVariableName;
            }
        }

        if (string.IsNullOrWhiteSpace(expression) || string.IsNullOrWhiteSpace(contextVariableName))
        {
            return Fail("Could not extract a LINQ query expression at the current caret location.");
        }

        var targetAssembly = AssemblyResolver.TryGetTargetAssembly(filePath);
        if (string.IsNullOrWhiteSpace(targetAssembly) ||
            targetAssembly.StartsWith("DEBUG_FAIL", StringComparison.Ordinal) ||
            !File.Exists(targetAssembly))
        {
            return Fail("Could not locate compiled target assembly for this file. Build the project and try again.");
        }

        var usingContext = LspSyntaxHelper.ExtractUsingContext(sourceText);

        try
        {
            var sw = Stopwatch.StartNew();
            Console.Error.WriteLine($"[QL-Hover] structured translate-start line={line} char={character} assembly={targetAssembly}");

            var translation = await _engine.TranslateAsync(new TranslationRequest
            {
                AssemblyPath = targetAssembly,
                Expression = expression,
                ContextVariableName = contextVariableName,
                AdditionalImports = usingContext.Imports.ToArray(),
                UsingAliases = new Dictionary<string, string>(usingContext.Aliases, StringComparer.Ordinal),
                UsingStaticTypes = usingContext.StaticTypes.ToArray(),
            }, cancellationToken);

            sw.Stop();
            Console.Error.WriteLine($"[QL-Hover] structured translate-finished line={line} char={character} success={translation.Success} elapsedMs={sw.ElapsedMilliseconds} commands={translation.Commands.Count}");

            if (!translation.Success)
            {
                Console.Error.WriteLine($"[QL-Hover] structured translate-error line={line} char={character} message={translation.ErrorMessage}");
                return Fail(translation.ErrorMessage ?? "Translation failed.");
            }

            var commands = translation.Commands.Count > 0
                ? translation.Commands
                : translation.Sql is null
                    ? []
                    : [new QuerySqlCommand { Sql = translation.Sql, Parameters = translation.Parameters }];

            if (commands.Count == 0)
            {
                Console.Error.WriteLine($"[QL-Hover] structured translate-empty-commands line={line} char={character}");
                return Fail("No SQL was produced for this expression.");
            }

            var providerName = translation.Metadata?.ProviderName;
            var statements = commands.Select((command, index) =>
            {
                var formatted = FormatSqlForDisplay(command.Sql.Trim(), providerName);
                var splitLabel = commands.Count > 1 ? $"Split Query {index + 1} of {commands.Count}" : null;
                return new QueryLensSqlStatement(formatted, splitLabel);
            }).ToList();

            var warnings = translation.Warnings.Select(w => string.IsNullOrWhiteSpace(w.Suggestion)
                ? $"{w.Code}: {w.Message}"
                : $"{w.Code}: {w.Message} ({w.Suggestion})").ToArray();

            Console.Error.WriteLine($"[QL-Hover] structured hover-ready line={line} char={character} commands={commands.Count}");
            return new QueryLensStructuredHoverResult(
                Success: true,
                ErrorMessage: null,
                Statements: statements,
                CommandCount: commands.Count,
                SourceExpression: expression,
                DbContextType: translation.Metadata?.DbContextType,
                ProviderName: translation.Metadata?.ProviderName,
                SourceFile: filePath,
                SourceLine: line + 1,
                Warnings: warnings,
                Mode: "direct");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[QL-Hover] structured translate-exception line={line} char={character} type={ex.GetType().Name} message={ex.Message}");
            return Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string BuildHoverMarkdown(
        IReadOnlyList<QuerySqlCommand> commands,
        IReadOnlyList<QueryWarning> warnings,
        string uri,
        int line,
        int character,
        string sourceExpression,
        string sourceFile,
        TranslationMetadata? metadata)
    {
        var providerName = metadata?.ProviderName;
        var sql = string.Join(
            "\n\n",
            commands.Select((command, index) =>
            {
                var raw = commands.Count == 1
                    ? command.Sql.Trim()
                    : $"-- Split Query {index + 1} of {commands.Count}\n{command.Sql.Trim()}";
                return FormatSqlForDisplay(raw, providerName);
            }));

        var statementWord = commands.Count == 1 ? "query" : "queries";
        var warningLines = warnings
            .Select(w => string.IsNullOrWhiteSpace(w.Suggestion)
                ? $"- {w.Code}: {w.Message}"
                : $"- {w.Code}: {w.Message} ({w.Suggestion})")
            .ToArray();

        var queryParams = $"uri={Uri.EscapeDataString(uri)}&line={line}&character={character}";
        var copyLink = $"[Copy SQL](efquerylens://copySql?{queryParams})";
        var openLink = $"[Open SQL Editor](efquerylens://openSqlEditor?{queryParams})";

        // Plain Markdown only (no HTML entities) so VS Code, VS, and Rider all render the same.
        var header = $"**QueryLens · {commands.Count} {statementWord}** | {copyLink} | {openLink}";

        var body = warningLines.Length == 0
            ? $"{header}\n\n```sql\n{sql}\n```"
            : $"{header}\n\n```sql\n{sql}\n```\n\n**Notes**\n{string.Join("\n", warningLines)}";

        // Append embedded metadata comment so all 3 clients can build enriched copy/open content.
        var metaPayload = new QueryLensHoverMetadataPayload
        {
            SourceExpression = sourceExpression,
            ExecutedExpression = sourceExpression,   // always same for now; future: rewrite detection
            Mode = "direct",
            ModeDescription = "Direct query translation",
            Warnings = warnings.Select(w => $"{w.Code}: {w.Message}").ToArray(),
            SourceFile = sourceFile,
            SourceLine = line + 1,                   // convert 0-based LSP line to 1-based display
            DbContextType = metadata?.DbContextType ?? string.Empty,
            ProviderName = metadata?.ProviderName ?? string.Empty,
        };
        var metaJson = JsonSerializer.Serialize(metaPayload);
        var metaBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(metaJson));

        return $"{body}\n\n<!-- QUERYLENS_META:{metaBase64} -->";
    }

    private static Dialect ResolveDialect(string? providerName) => providerName switch
    {
        { } p when p.Contains("MySql", StringComparison.OrdinalIgnoreCase)
                || p.Contains("MariaDb", StringComparison.OrdinalIgnoreCase) => Dialect.MySql,
        { } p when p.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                || p.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase)
                || p.Contains("Postgres", StringComparison.OrdinalIgnoreCase) => Dialect.PostgreSql,
        { } p when p.Contains("SqlServer", StringComparison.OrdinalIgnoreCase)
                || p.Contains("SqlCe", StringComparison.OrdinalIgnoreCase) => Dialect.TSql,
        _ => Dialect.StandardSql,
    };

    private static string FormatSqlForDisplay(string sql, string? providerName = null)
    {
        if (string.IsNullOrWhiteSpace(sql)) return sql;
        try
        {
            var dialect = ResolveDialect(providerName);
            return SqlFormatter.Of(dialect)
                .Format(sql, FormatConfig.Builder()
                    .Indent("  ")
                    .MaxColumnLength(120)
                    .Build());
        }
        catch
        {
            // Fallback: simple clause-break regex so hover still shows something readable.
            var s = sql.Trim();
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+FROM\s+", "\nFROM ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+WHERE\s+", "\nWHERE ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+GROUP BY\s+", "\nGROUP BY ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+ORDER BY\s+", "\nORDER BY ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+LEFT OUTER JOIN\s+", "\n  LEFT OUTER JOIN ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+RIGHT OUTER JOIN\s+", "\n  RIGHT OUTER JOIN ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+INNER JOIN\s+", "\n  INNER JOIN ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+JOIN\s+", "\n  JOIN ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return s.Trim();
        }
    }

}
