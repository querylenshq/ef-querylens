namespace EFQueryLens.VisualStudio.Host.Contracts;

internal static class QueryLensHostStatusMapper
{
    internal sealed class MappedStatus
    {
        public MappedStatus(
            string text,
            string displayText,
            QueryLensHostState state,
            string tooltip)
        {
            Text = text;
            DisplayText = displayText;
            State = state;
            Tooltip = tooltip;
        }

        public string Text { get; }

        public string DisplayText { get; }

        public QueryLensHostState State { get; }

        public string Tooltip { get; }
    }

    public static MappedStatus Map(QueryLensHostStatusSnapshot? snapshot)
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

        var displayText = $"{GetDisplayMarker(state)} QueryLens";
        var tooltipParts = new System.Collections.Generic.List<string> { $"State: {text}", message };
        if (!string.IsNullOrWhiteSpace(assembly))
        {
            tooltipParts.Add($"Assembly: {assembly}");
        }

        if (inflight > 0)
        {
            tooltipParts.Add($"In flight: {inflight}");
        }

        tooltipParts.Add("Click to open EF QueryLens output");
        return new MappedStatus(text, displayText, state, string.Join("\n", tooltipParts));
    }

    private static QueryLensHostState NormalizeHostState(QueryLensHostState? raw) =>
        raw ?? QueryLensHostState.Starting;

    private static string GetDisplayMarker(QueryLensHostState state) =>
        state switch
        {
            QueryLensHostState.Warming => "[W]",
            QueryLensHostState.Computing => "[C]",
            QueryLensHostState.Ready => "[R]",
            QueryLensHostState.Unavailable => "[!]",
            _ => "[S]",
        };
}
