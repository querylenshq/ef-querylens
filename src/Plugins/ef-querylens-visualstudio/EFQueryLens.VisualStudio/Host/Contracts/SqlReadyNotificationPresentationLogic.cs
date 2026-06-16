namespace EFQueryLens.VisualStudio.Host.Contracts;

internal static class SqlReadyNotificationPresentationLogic
{
    internal static bool ShouldUseLayeredCues(bool notifyEnabled) => notifyEnabled;

    internal static string BuildStatusBarMessage(QueryLensHostSqlReadyNotification notification)
        => QueryLensHostSqlReadyNotificationLogic.BuildToastMessage(notification);

    internal static string BuildOutputLine(QueryLensHostSqlReadyNotification notification)
    {
        var fileName = string.IsNullOrWhiteSpace(notification.FileName) ? "query" : notification.FileName.Trim();
        var lineNumber = notification.Line + 1;
        return $"SQL ready: {fileName}:{lineNumber}";
    }

    internal static bool ShouldRestoreStatusBarFlash(int currentGeneration, int restoreGeneration)
        => currentGeneration == restoreGeneration;
}
