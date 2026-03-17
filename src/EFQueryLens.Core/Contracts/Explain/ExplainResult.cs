using EFQueryLens.Core.Contracts;

namespace EFQueryLens.Core.Contracts.Explain;

public sealed record ExplainResult
{
    public required QueryTranslationResult Translation { get; init; }

    public ExplainNode? Plan { get; init; }

    /// <summary>
    /// false means the plan contains estimates only (EXPLAIN without ANALYZE).
    /// </summary>
    public bool IsActualExecution { get; init; }

    public string? ServerVersion { get; init; }

    public bool Success => Translation.Success;

    public string? Sql => Translation.Sql;

    public string? ErrorMessage => Translation.ErrorMessage;

    public TranslationMetadata Metadata => Translation.Metadata;
}
