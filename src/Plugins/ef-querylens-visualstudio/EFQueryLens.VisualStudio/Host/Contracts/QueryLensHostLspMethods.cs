namespace EFQueryLens.VisualStudio.Host.Contracts;

/// <summary>Stable LSP method and notification names (wire contract; mirrored from shared protocol).</summary>
internal static class QueryLensHostLspMethods
{
    public const string StatusRequest = "efquerylens/status";
    public const string WarmupRequest = "efquerylens/warmup";
    public const string HoverRequest = "efquerylens/hover";
    public const string DaemonRestartRequest = "efquerylens/daemon/restart";
    public const string PreviewRecalculateRequest = "efquerylens/preview/recalculate";
    public const string SetupDetectRequest = "efquerylens/setup/detect";
    public const string SetupApplyRequest = "efquerylens/setup/apply";
    public const string SetupRequest = "efquerylens/setup";

    public const string SqlReadyNotification = "efquerylens/sqlReady";
    public const string StatusChangedNotification = "efquerylens/statusChanged";
    public const string ShowSqlPreviewNotification = "efquerylens/showSqlPreview";
    public const string ShowSqlPopupNotification = "efquerylens/showSqlPopup";
    public const string CopySqlToClipboardNotification = "efquerylens/copySqlToClipboard";
}
