using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace EFQueryLens.VisualStudio.Host.Contracts;

[JsonConverter(typeof(StringEnumConverter))]
internal enum QueryLensHostState
{
    Starting = 0,
    Warming = 1,
    Ready = 2,
    Computing = 3,
    Unavailable = 4,
}

internal sealed class QueryLensHostStatusSnapshot
{
    public QueryLensHostStatusSnapshot()
    {
    }

    public QueryLensHostStatusSnapshot(
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
