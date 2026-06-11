using System.Reflection;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp.HoverPipeline;
using EFQueryLens.Lsp.Parsing;

namespace EFQueryLens.Core.Tests.Lsp;

public class HoverCacheKeyTests
{
    [Fact]
    public void TryGetAssemblyFingerprint_NonExistentFile_ReturnsNull()
    {
        var result = AssemblyResolver.TryGetAssemblyFingerprint(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cs"));
        Assert.Null(result);
    }

    [Fact]
    public void TryGetAssemblyFingerprint_ExistingAssembly_ReturnsFourPartKeyWithBundleKey()
    {
        using var tempAssembly = new TempFile(".dll");
        File.WriteAllBytes(tempAssembly.Path, [0x4D, 0x5A]);

        var fingerprint = AssemblyResolver.TryGetAssemblyFingerprint(tempAssembly.Path + ".cs");
        Assert.Null(fingerprint);

        var direct = BuildFingerprintForFile(tempAssembly.Path);
        Assert.NotNull(direct);
        var parts = direct!.Split('|');
        Assert.Equal(4, parts.Length);
        Assert.Equal(Path.GetFullPath(tempAssembly.Path), parts[0], StringComparer.OrdinalIgnoreCase);
        Assert.True(long.TryParse(parts[1], out _));
        Assert.True(long.TryParse(parts[2], out var ticks) && ticks > 0);
        Assert.False(string.IsNullOrWhiteSpace(parts[3]));
    }

    [Fact]
    public void BuildCacheKey_SameSemanticKeyAndFingerprint_ProducesStableKey()
    {
        var key1 = HoverResultCache.BuildCacheKey("fp|1|2", "sem-abc");
        var key2 = HoverResultCache.BuildCacheKey("fp|1|2", "sem-abc");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void BuildCacheKey_DifferentSemanticKey_DifferentKey()
    {
        var key1 = HoverResultCache.BuildCacheKey("fp|1|2", "sem-a");
        var key2 = HoverResultCache.BuildCacheKey("fp|1|2", "sem-b");
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void BuildCacheKey_DifferentFingerprint_DifferentKey()
    {
        var key1 = HoverResultCache.BuildCacheKey("fp|1|2", "sem-a");
        var key2 = HoverResultCache.BuildCacheKey("fp|9|9", "sem-a");
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void BuildCacheKey_NoPositionComponent_OnlyFingerprintAndSemanticKey()
    {
        var key = HoverResultCache.BuildCacheKey("no-assembly|C:\\file.cs", "sem-key");
        Assert.Equal("no-assembly|C:\\file.cs|sem-key", key);
        Assert.DoesNotContain("cursor", key, StringComparison.OrdinalIgnoreCase);
    }

    private static string? BuildFingerprintForFile(string path)
    {
        if (!File.Exists(path)) return null;
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        var bundleKey = AssemblyBundleRevision.TryPeekBundleKey(fullPath) ?? "unknown";
        return $"{fullPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{bundleKey}";
    }

    private sealed class TempFile(string extension) : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + extension);
        public void Dispose() { try { File.Delete(Path); } catch { } }
    }
}
