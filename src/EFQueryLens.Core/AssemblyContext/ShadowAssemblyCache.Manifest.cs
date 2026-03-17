using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace EFQueryLens.Core.AssemblyContext;

internal sealed partial class ShadowAssemblyCache
{
    private static List<ManifestEntry> BuildManifest(string sourceDirectory)
    {
        return Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new ManifestEntry(
                    Path.GetFullPath(path),
                    Path.GetRelativePath(sourceDirectory, path),
                    info.Length,
                    info.LastWriteTimeUtc.Ticks);
            })
            .OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ComputeBundleKey(string sourceDirectory, IReadOnlyList<ManifestEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append(Path.GetFullPath(sourceDirectory)).Append('|');
        foreach (var entry in entries)
        {
            sb.Append(entry.RelativePath).Append('|')
              .Append(entry.Length).Append('|')
              .Append(entry.LastWriteTicksUtc).Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    private static void TryAtomicPromote(string stagingPath, string finalPath)
    {
        for (var i = 0; i < 5; i++)
        {
            try
            {
                Directory.Move(stagingPath, finalPath);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (Directory.Exists(finalPath))
                {
                    return;
                }

                if (i == 4)
                {
                    throw;
                }

                Thread.Sleep(50 * (i + 1));
            }
        }
    }

    private sealed record ManifestEntry(string FullPath, string RelativePath, long Length, long LastWriteTicksUtc);
}
