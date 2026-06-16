namespace EFQueryLens.VisualStudio.Host.Contracts;

internal static class SqlReadyWatchBudget
{
    /// <summary>Default daemon translate timeout — watcher must outlive QuickInfo poll budget.</summary>
    private const int DefaultTranslateTimeoutMs = 15_000;

    private const int MaximumNotificationWaitMs = 120_000;
    private const int FloorMs = 500;

    internal static int ComputeNotificationWaitMs(int hoverWaitWhenWarmMs)
    {
        var budget = Math.Max(hoverWaitWhenWarmMs, DefaultTranslateTimeoutMs);
        return Math.Min(Math.Max(budget, FloorMs), MaximumNotificationWaitMs);
    }
}
