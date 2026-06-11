using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace EFQueryLens.Lsp.Protocol;

/// <summary>Global QueryLens host readiness shown in IDE status bars.</summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum QueryLensHostState
{
    Starting = 0,
    Warming = 1,
    Ready = 2,
    Computing = 3,
    Unavailable = 4,
}

public sealed class QueryLensStatusSnapshot
{
    public QueryLensStatusSnapshot()
    {
    }

    public QueryLensStatusSnapshot(
        QueryLensHostState state,
        string message,
        string? assemblyPath = null,
        int inflightCount = 0,
        bool warmed = false)
    {
        State = state;
        Message = message ?? string.Empty;
        AssemblyPath = assemblyPath;
        InflightCount = inflightCount;
        Warmed = warmed;
    }

    public QueryLensHostState State { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? AssemblyPath { get; set; }

    public int InflightCount { get; set; }

    public bool Warmed { get; set; }
}
