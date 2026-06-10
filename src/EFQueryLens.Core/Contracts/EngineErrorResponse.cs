namespace EFQueryLens.Core.Contracts;

/// <summary>
/// Structured error body returned by the QueryLens daemon for non-success HTTP responses.
/// </summary>
public sealed record EngineErrorResponse
{
    public string? ErrorType { get; init; }

    public string? FailureKind { get; init; }

    public string? Message { get; init; }
}
