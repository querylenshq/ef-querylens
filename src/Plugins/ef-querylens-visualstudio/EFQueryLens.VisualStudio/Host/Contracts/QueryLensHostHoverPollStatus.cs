namespace EFQueryLens.VisualStudio.Host.Contracts;

internal static class QueryLensHostHoverPollStatus
{
    internal const int Ready = 0;
    internal const int InQueue = 1;
    internal const int Starting = 2;
    internal const int DaemonUnavailable = 3;
}

internal sealed class QueryLensHostHoverPollResult
{
    public bool Success { get; set; }

    public int CommandCount { get; set; }

    public int Status { get; set; }

    internal static bool IsQueued(int status) =>
        status is QueryLensHostHoverPollStatus.InQueue or QueryLensHostHoverPollStatus.Starting;

    internal static bool IsTerminal(int status) =>
        status is QueryLensHostHoverPollStatus.Ready or QueryLensHostHoverPollStatus.DaemonUnavailable;
}
