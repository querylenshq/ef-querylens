namespace EFQueryLens.Lsp.HoverPipeline;

/// <summary>
/// A resolved LINQ query region: statement span, semantic translation identity,
/// and pre-extracted expression metadata shared across cursor positions.
/// </summary>
internal sealed record QueryRegion(
    string SemanticKey,
    string RegionKey,
    string AssemblyFingerprint,
    int AnchorLine,
    int AnchorCharacter,
    string Expression,
    string ContextVariableName)
{
    public QueryRegion WithRequestPosition(int requestLine, int requestCharacter)
        => this with { RequestLine = requestLine, RequestCharacter = requestCharacter };

    public int RequestLine { get; init; }
    public int RequestCharacter { get; init; }
}
