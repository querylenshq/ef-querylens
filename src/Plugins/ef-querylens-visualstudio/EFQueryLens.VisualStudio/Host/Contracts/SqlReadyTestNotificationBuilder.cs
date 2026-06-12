using System;
using System.IO;

namespace EFQueryLens.VisualStudio.Host.Contracts;

internal static class SqlReadyTestNotificationBuilder
{
    private const string FallbackFileUri = "file:///Test.cs";
    private const string FallbackFileName = "Test.cs";

    internal static QueryLensHostSqlReadyNotification Build(string? filePath, int line, int character)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new QueryLensHostSqlReadyNotification(
                FallbackFileUri,
                line,
                character,
                FallbackFileName,
                commandCount: 1);
        }

        var normalizedPath = filePath.Replace('\\', Path.DirectorySeparatorChar);
        var fileUri = new Uri(Path.GetFullPath(normalizedPath)).AbsoluteUri;
        var fileName = Path.GetFileName(normalizedPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = FallbackFileName;
        }

        return new QueryLensHostSqlReadyNotification(fileUri, line, character, fileName, commandCount: 1);
    }
}
