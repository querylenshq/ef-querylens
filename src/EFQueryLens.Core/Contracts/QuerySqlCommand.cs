namespace EFQueryLens.Core.Contracts;

public sealed record QuerySqlCommand
{
    public required string Sql { get; init; }
    public IReadOnlyList<QueryParameter> Parameters { get; init; } = [];
}
