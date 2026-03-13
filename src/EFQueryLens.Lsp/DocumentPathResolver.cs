namespace EFQueryLens.Lsp;

using System;
using System.IO;

internal static class DocumentPathResolver
{
    public static string Resolve(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var rawPath = uri.IsAbsoluteUri
            ? Uri.UnescapeDataString(uri.AbsolutePath)
            : Uri.UnescapeDataString(uri.OriginalString);

        var candidate = string.IsNullOrWhiteSpace(rawPath)
            ? uri.LocalPath
            : rawPath;

        if (OperatingSystem.IsWindows())
        {
            candidate = candidate.Replace('/', '\\');

            // file:///c%3A/foo.cs decodes to "\\c:\\foo.cs" on Windows-style flows.
            if (candidate.Length >= 3
                && candidate[0] == '\\'
                && char.IsLetter(candidate[1])
                && candidate[2] == ':')
            {
                candidate = candidate[1..];
            }

            // Defensive cleanup for accidentally duplicated drive-prefix forms.
            if (candidate.Length >= 5
                && char.IsLetter(candidate[0])
                && candidate[1] == ':'
                && candidate[2] == '\\'
                && char.IsLetter(candidate[3])
                && candidate[4] == ':')
            {
                candidate = candidate[3..];
            }
        }

        try
        {
            return Path.GetFullPath(candidate);
        }
        catch
        {
            return candidate;
        }
    }

    public static string ToUri(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        try
        {
            // Ensure path is absolute and normalized
            var fullPath = Path.GetFullPath(filePath);
            return new Uri(fullPath).ToString();
        }
        catch
        {
            return filePath;
        }
    }
}
