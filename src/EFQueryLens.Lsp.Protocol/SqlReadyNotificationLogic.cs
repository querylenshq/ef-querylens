using System;
using System.Collections.Generic;

namespace EFQueryLens.Lsp.Protocol;

/// <summary>Dedupe and message helpers for SQL-ready notifications.</summary>
public static class SqlReadyNotificationLogic
{
    public const string GoToQueryActionTitle = "Go to Query";
    public const string OpenSqlActionTitle = "Open SQL";
    private const int DedupeWindowMs = 30_000;

    private static readonly Dictionary<string, long> RecentNotifications =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when the notification represents a successful SQL translation (not a factory prompt or error hover).
    /// </summary>
    public static bool ShouldNotify(SqlReadyNotification? payload)
        => payload is not null && payload.CommandCount > 0;

    public static bool ShouldShow(SqlReadyNotification payload, bool enabled, long nowMs)
    {
        if (!enabled || !ShouldNotify(payload))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.FileUri))
        {
            return false;
        }

        var key = $"{payload.FileUri}|{payload.Line}|{payload.Character}";
        if (RecentNotifications.TryGetValue(key, out var lastShown)
            && nowMs - lastShown < DedupeWindowMs)
        {
            return false;
        }

        RecentNotifications[key] = nowMs;
        return true;
    }

    public static string BuildToastMessage(SqlReadyNotification payload)
    {
        var fileName = string.IsNullOrWhiteSpace(payload.FileName) ? "query" : payload.FileName.Trim();
        var lineNumber = payload.Line + 1;
        return $"EF QueryLens: SQL ready — {fileName}:{lineNumber}";
    }

    public static void ResetDedupeForTests() => RecentNotifications.Clear();
}
