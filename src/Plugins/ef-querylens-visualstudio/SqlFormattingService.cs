using System.Text.RegularExpressions;
using SQL.Formatter;

namespace EFQueryLens.VisualStudio;

internal static class SqlFormattingService
{
    private static readonly Regex TSqlIdentifierRegex = new(@"\[[^\]]+\]", RegexOptions.Compiled);

    public static string Format(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return sql;
        }

        try
        {
            var dialect = ResolveDialectName(sql);
            var formatted = SqlFormatter.Of(dialect).Format(sql);
            if (string.IsNullOrWhiteSpace(formatted))
            {
                return sql;
            }

            return formatted;
        }
        catch (Exception ex)
        {
            QueryLensLogger.Error("sql-format-failed", ex);
            return sql;
        }
    }

    private static string ResolveDialectName(string sql)
    {
        if (sql.Contains('`'))
        {
            return "mysql";
        }

        if (TSqlIdentifierRegex.IsMatch(sql))
        {
            return "tsql";
        }

        return "sql";
    }
}
