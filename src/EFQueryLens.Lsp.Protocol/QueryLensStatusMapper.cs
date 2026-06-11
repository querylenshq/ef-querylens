namespace EFQueryLens.Lsp.Protocol;

/// <summary>Maps server status snapshots to IDE-neutral display strings.</summary>
public static class QueryLensStatusMapper
{
    public sealed class MappedStatus
    {
        public MappedStatus(string text, string tooltip)
        {
            Text = text;
            Tooltip = tooltip;
        }

        public string Text { get; }

        public string Tooltip { get; }
    }

    public static MappedStatus Map(QueryLensStatusSnapshot? snapshot)
    {
        var warmed = snapshot?.Warmed == true;
        var state = warmed
            ? NormalizeHostState(snapshot?.State)
            : snapshot?.State == QueryLensHostState.Unavailable
                ? QueryLensHostState.Unavailable
                : snapshot?.State == QueryLensHostState.Computing
                    ? QueryLensHostState.Computing
                    : QueryLensHostState.Warming;

        var message = string.IsNullOrWhiteSpace(snapshot?.Message)
            ? "Starting QueryLens…"
            : snapshot!.Message.Trim();

        var assembly = snapshot?.AssemblyPath?.Trim();
        var inflight = snapshot?.InflightCount ?? 0;

        var text = state switch
        {
            QueryLensHostState.Warming => "QueryLens: Warming…",
            QueryLensHostState.Computing => "QueryLens: Computing SQL…",
            QueryLensHostState.Ready => "QueryLens: Ready",
            QueryLensHostState.Unavailable => "QueryLens: Unavailable",
            _ => "QueryLens: Starting…",
        };

        var tooltipParts = new System.Collections.Generic.List<string> { message };
        if (!string.IsNullOrWhiteSpace(assembly))
        {
            tooltipParts.Add($"Assembly: {assembly}");
        }

        if (inflight > 0)
        {
            tooltipParts.Add($"In flight: {inflight}");
        }

        tooltipParts.Add("Click to open EF QueryLens output");
        return new MappedStatus(text, string.Join("\n", tooltipParts));
    }

    private static QueryLensHostState NormalizeHostState(QueryLensHostState? raw) =>
        raw ?? QueryLensHostState.Starting;
}
