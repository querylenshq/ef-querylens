using EFQueryLens.Core.AssemblyContext;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Core.Tests.Scripting;

namespace EFQueryLens.Core.Tests.AssemblyContext;

public class AssemblyReflectionTests
{
    [Fact]
    public void GetLoadableTypes_ReturnsTypesFromTestAssembly()
    {
        var types = AssemblyReflection.GetLoadableTypes(typeof(AssemblyReflectionTests).Assembly);
        Assert.Contains(types, t => t == typeof(AssemblyReflectionTests));
    }

    [Fact]
    public void IsIgnorableReflectionFailure_TreatsLoaderFailuresAsOptionalScanSkips()
    {
        Assert.True(AssemblyReflection.IsIgnorableReflectionFailure(new FileNotFoundException("missing.dll")));
        Assert.True(AssemblyReflection.IsIgnorableReflectionFailure(new TypeLoadException("type load failed")));
    }

    [Fact]
    public void GetCachedLoadableTypes_ReusesManifestForSameAssembly()
    {
        AssemblyReflection.ClearManifestCacheForTests();
        var assembly = typeof(AssemblyReflectionTests).Assembly;

        var first = AssemblyReflection.GetCachedLoadableTypes(assembly);
        var second = AssemblyReflection.GetCachedLoadableTypes(assembly);

        Assert.Same(first, second);
    }

    [Fact]
    public void AssemblyBundleRevision_TryPeekBundleKey_MatchesTryBuild()
    {
        var sampleDll = QueryEvaluatorTests.GetSampleMySqlAppDll();
        if (!File.Exists(sampleDll))
        {
            return;
        }

        var peek = AssemblyBundleRevision.TryPeekBundleKey(sampleDll);
        Assert.NotNull(peek);

        var revision = AssemblyBundleRevision.TryBuild(sampleDll);
        if (revision is not null)
        {
            Assert.Equal(peek, revision.BundleKey);
        }
    }
}
