namespace EFQueryLens.Lsp.HoverPipeline;

internal sealed record SqlReadyNotification(
    string FileUri,
    int Line,
    int Character,
    string FileName,
    int CommandCount);
