using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace EFQueryLens.Lsp.Services;

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

public sealed record QueryLensStatusSnapshot(
    QueryLensHostState State,
    string Message,
    string? AssemblyPath = null,
    int InflightCount = 0,
    bool Warmed = false);
