using EFQueryLens.Core;
using EFQueryLens.Lsp.Parsing;
using System.Diagnostics;

namespace EFQueryLens.Lsp.Services;

internal sealed record HoverPreviewComputationResult(bool Success, string Output);

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
            var markdown = BuildHoverMarkdown(commands, translation.Warnings, documentUri, line, character);
            Console.Error.WriteLine($"[QL-Hover] hover-markdown-ready line={line} char={character} markdownLen={markdown.Length}");
            return new HoverPreviewComputationResult(true, markdown);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[QL-Hover] translate-exception line={line} char={character} type={ex.GetType().Name} message={ex.Message}");
            return new HoverPreviewComputationResult(false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string BuildHoverMarkdown(IReadOnlyList<QuerySqlCommand> commands, IReadOnlyList<QueryWarning> warnings, string uri, int line, int character)
    {
        var sql = string.Join(
            "\n\n",
            commands.Select((command, index) =>
            {
                var raw = commands.Count == 1
                    ? command.Sql.Trim()
                    : $"-- Split Query {index + 1} of {commands.Count}\n{command.Sql.Trim()}";
                return FormatSqlForDisplay(raw);
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

        if (warningLines.Length == 0)
        {
            return $"{header}\n\n```sql\n{sql}\n```";
        }

        return $"{header}\n\n```sql\n{sql}\n```\n\n**Notes**\n{string.Join("\n", warningLines)}";
    }

    /// <summary>
    /// Inserts newlines before major SQL clauses so hover and Preview popup show readable, consistently formatted SQL.
    /// </summary>
    private static string FormatSqlForDisplay(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return sql;
        var s = sql.Trim();
        // Clause-level breaks (case-insensitive): newline before FROM, WHERE, GROUP BY, ORDER BY, JOIN variants
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
