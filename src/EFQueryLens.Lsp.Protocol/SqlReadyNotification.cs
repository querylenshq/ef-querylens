namespace EFQueryLens.Lsp.Protocol;

/// <summary>Payload for <see cref="LspProtocolMethods.SqlReadyNotification"/>.</summary>
public sealed class SqlReadyNotification
{
    public SqlReadyNotification()
    {
    }

    public SqlReadyNotification(
        string fileUri,
        int line,
        int character,
        string fileName,
        int commandCount)
    {
        FileUri = fileUri;
        Line = line;
        Character = character;
        FileName = fileName;
        CommandCount = commandCount;
    }

    public string FileUri { get; set; } = string.Empty;

    public int Line { get; set; }

    public int Character { get; set; }

    public string FileName { get; set; } = string.Empty;

    public int CommandCount { get; set; }
}
