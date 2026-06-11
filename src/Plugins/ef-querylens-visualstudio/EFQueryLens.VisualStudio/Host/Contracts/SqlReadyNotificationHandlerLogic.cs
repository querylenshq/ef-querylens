namespace EFQueryLens.VisualStudio.Host.Contracts;

internal static class SqlReadyNotificationHandlerLogic
{
    internal static bool TryPrepareShow(
        QueryLensHostSqlReadyNotification notification,
        bool enabled,
        long nowMs,
        out string message)
    {
        message = string.Empty;

        if (!enabled || !QueryLensHostSqlReadyNotificationLogic.ShouldNotify(notification))
        {
            return false;
        }

        if (!QueryLensHostSqlReadyNotificationLogic.ShouldShow(notification, enabled, nowMs))
        {
            return false;
        }

        message = QueryLensHostSqlReadyNotificationLogic.BuildToastMessage(notification);
        return true;
    }
}
