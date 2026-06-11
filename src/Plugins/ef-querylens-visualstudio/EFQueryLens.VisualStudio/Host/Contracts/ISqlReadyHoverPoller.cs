namespace EFQueryLens.VisualStudio.Host.Contracts;

internal interface ISqlReadyHoverPoller
{
    Task<QueryLensHostHoverPollResult?> PollAsync(
        string filePath,
        int line,
        int character,
        CancellationToken cancellationToken);
}

internal interface ISqlReadyNotificationSink
{
    void RaiseFromWatch(string filePath, int line, int character, QueryLensHostHoverPollResult response);
}
