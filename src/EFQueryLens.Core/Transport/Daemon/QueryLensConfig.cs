namespace EFQueryLens.Core.Daemon;

/// <summary>
/// Root configuration loaded from .querylens.json in a workspace.
/// </summary>
public sealed record QueryLensConfig
{
    public IReadOnlyList<QueryLensContextConfig> Contexts { get; init; } = [];
}

public sealed record QueryLensContextConfig
{
    public required string Name { get; init; }
    public required string Assembly { get; init; }
    public string? DbContextType { get; init; }
    public string? Provider { get; init; }
    public IReadOnlyList<string> AssemblySources { get; init; } = [];
}
