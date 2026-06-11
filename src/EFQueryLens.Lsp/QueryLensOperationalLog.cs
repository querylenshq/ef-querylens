namespace EFQueryLens.Lsp;

/// <summary>
/// Lightweight, always-on operational tracing (one line per user-visible action).
/// Full verbose tracing remains behind <c>QUERYLENS_DEBUG</c>.
/// </summary>
internal static class QueryLensOperationalLog
{
    internal static void Info(string message)
        => Console.Error.WriteLine($"[QL-Ops] {message}");
}
