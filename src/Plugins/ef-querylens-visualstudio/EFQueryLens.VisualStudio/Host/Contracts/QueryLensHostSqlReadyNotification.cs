using Newtonsoft.Json;

namespace EFQueryLens.VisualStudio.Host.Contracts;

internal sealed class QueryLensHostSqlReadyNotification
{
    public QueryLensHostSqlReadyNotification()
    {
    }

    public QueryLensHostSqlReadyNotification(
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

    [JsonProperty("fileUri")]
    public string FileUri { get; set; } = string.Empty;

    [JsonProperty("line")]
    public int Line { get; set; }

    [JsonProperty("character")]
    public int Character { get; set; }

    [JsonProperty("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonProperty("commandCount")]
    public int CommandCount { get; set; }
}
