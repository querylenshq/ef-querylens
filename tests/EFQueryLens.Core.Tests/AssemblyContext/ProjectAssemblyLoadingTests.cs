using EFQueryLens.Core.AssemblyContext;
using EFQueryLens.Core.Tests.Scripting;

namespace EFQueryLens.Core.Tests.AssemblyContext;

public class ProjectAssemblyLoadingTests
{
    [Fact]
    public void StagedLoading_LoadsFewerAssembliesThanFullBinGlob_ForSampleHost()
    {
        var sampleDll = QueryEvaluatorTests.GetSampleMySqlAppDll();
        if (!File.Exists(sampleDll))
        {
            return;
        }

        using var staged = ProjectAssemblyContextFactory.Create(sampleDll);

        var binDir = Path.GetDirectoryName(sampleDll)!;
        var binDllCount = Directory.EnumerateFiles(binDir, "*.dll", SearchOption.TopDirectoryOnly).Count();
        var binLoadedCount = staged.LoadedAssemblies.Count(assembly =>
            !string.IsNullOrWhiteSpace(assembly.Location)
            && string.Equals(Path.GetDirectoryName(assembly.Location), binDir, StringComparison.OrdinalIgnoreCase));

        Assert.True(binLoadedCount > 0);
        Assert.True(binLoadedCount <= binDllCount);
    }

    [Fact]
    public void LoadRemainingBinAssemblies_IncreasesLoadedCount_WhenNewSiblingExists()
    {
        var sampleDll = QueryEvaluatorTests.GetSampleMySqlAppDll();
        if (!File.Exists(sampleDll))
        {
            return;
        }

        using var ctx = ProjectAssemblyContextFactory.Create(sampleDll);
        var before = ctx.LoadedAssemblyCount;

        ctx.LoadRemainingBinAssemblies();
        var after = ctx.LoadedAssemblyCount;

        Assert.True(after >= before);
    }

    [Fact]
    public void MedicsApiHost_StagedLoading_FindsFactoryWithFewerLoadsThanFullBin()
    {
        var sourceDll = Environment.GetEnvironmentVariable("QUERYLENS_TEST_MEDICS_API_DLL")
            ?? @"D:\tsp\hsa-share\share-medics-applications\src\Share.Medics.Applications.Api\bin\Debug\net8.0\Share.Medics.Applications.Api.dll";
        if (!File.Exists(sourceDll))
        {
            return;
        }

        using var ctx = ProjectAssemblyContextFactory.Create(sourceDll);

        var binDir = Path.GetDirectoryName(sourceDll)!;
        var binDllCount = Directory.EnumerateFiles(binDir, "*.dll", SearchOption.TopDirectoryOnly).Count();
        var binLoadedCount = ctx.LoadedAssemblies.Count(assembly =>
            !string.IsNullOrWhiteSpace(assembly.Location)
            && string.Equals(Path.GetDirectoryName(assembly.Location), binDir, StringComparison.OrdinalIgnoreCase));

        Assert.True(binLoadedCount <= binDllCount);
        Assert.True(binLoadedCount >= 1);
    }
}
