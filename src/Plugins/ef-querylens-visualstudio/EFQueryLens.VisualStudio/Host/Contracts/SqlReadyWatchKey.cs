namespace EFQueryLens.VisualStudio.Host.Contracts;

internal static class SqlReadyWatchKey
{
    internal static string Build(string fileUri, int line, int character) =>
        $"{fileUri}|{line}|{character}";
}
