namespace EFQueryLens.VisualStudio.Host.Contracts;

internal static class SqlReadyWatchBudget
{
    private const int MinimumNotificationWaitMs = 60_000;
    private const int MaximumNotificationWaitMs = 120_000;
    private const int FloorMs = 500;

    internal static int ComputeNotificationWaitMs(int hoverWaitWhenWarmMs)
    {
        var boosted = Math.Max(hoverWaitWhenWarmMs, MinimumNotificationWaitMs);
        return Math.Min(Math.Max(boosted, FloorMs), MaximumNotificationWaitMs);
    }
}
