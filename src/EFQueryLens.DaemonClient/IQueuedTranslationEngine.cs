using EFQueryLens.Core.Contracts;

namespace EFQueryLens.DaemonClient;

/// <summary>
/// Optional transport-layer capability for queued translation polling.
/// Not part of the core engine contract because queueing belongs to daemon hosts.
/// </summary>
public interface IQueuedTranslationEngine
{
    Task<QueuedTranslationResult> TranslateQueuedAsync(
        TranslationRequest request,
        CancellationToken ct = default);
}
