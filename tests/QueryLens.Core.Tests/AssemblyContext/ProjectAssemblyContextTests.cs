using QueryLens.Core.AssemblyContext;

namespace QueryLens.Core.Tests.AssemblyContext;

/// <summary>
/// Unit tests for <see cref="ProjectAssemblyContext"/>.
///
/// SampleApp.dll lands next to the test binary at build time because
/// QueryLens.Core.Tests.csproj references SampleApp with
/// ReferenceOutputAssembly="false". We locate it relative to this assembly's
/// location at runtime.
/// </summary>
public class ProjectAssemblyContextTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the absolute path to SampleApp.dll in the test output directory.
    /// </summary>
    private static string GetSampleAppDll()
    {
        var testDir = Path.GetDirectoryName(
            typeof(ProjectAssemblyContextTests).Assembly.Location)!;

        var dll = Path.Combine(testDir, "SampleApp.dll");

        if (!File.Exists(dll))
            throw new FileNotFoundException(
                "SampleApp.dll not found in test output directory. " +
                "Make sure the solution is built before running tests. " +
                $"Expected: {dll}");

        return dll;
    }

    // ─── Constructor / basic load ─────────────────────────────────────────────

    [Fact]
    public void Constructor_ValidAssembly_DoesNotThrow()
    {
        var dll = GetSampleAppDll();
        using var ctx = new ProjectAssemblyContext(dll);
        Assert.Equal(dll, ctx.AssemblyPath);
    }

    [Fact]
    public void Constructor_MissingFile_ThrowsFileNotFoundException()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "DoesNotExist.dll");
        Assert.Throws<FileNotFoundException>(() => new ProjectAssemblyContext(bogus));
    }

    [Fact]
    public void Constructor_EmptyPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ProjectAssemblyContext(""));
    }

    [Fact]
    public void AssemblyTimestamp_IsSet()
    {
        var dll = GetSampleAppDll();
        using var ctx = new ProjectAssemblyContext(dll);
        Assert.NotEqual(default, ctx.AssemblyTimestamp);
    }

    // ─── FindDbContextTypes ───────────────────────────────────────────────────

    [Fact]
    public void FindDbContextTypes_SampleApp_FindsExactlyOneContext()
    {
        var dll = GetSampleAppDll();
        using var ctx = new ProjectAssemblyContext(dll);

        var types = ctx.FindDbContextTypes();

        Assert.Single(types);
    }

    [Fact]
    public void FindDbContextTypes_SampleApp_FindsAppDbContext()
    {
        var dll = GetSampleAppDll();
        using var ctx = new ProjectAssemblyContext(dll);

        var types = ctx.FindDbContextTypes();

        Assert.Contains(types, t => t.Name == "AppDbContext");
    }

    [Fact]
    public void FindDbContextTypes_SampleApp_FullNameIsCorrect()
    {
        var dll = GetSampleAppDll();
        using var ctx = new ProjectAssemblyContext(dll);

        var types = ctx.FindDbContextTypes();

        Assert.Contains(types, t => t.FullName == "SampleApp.AppDbContext");
    }

    // ─── FindDbContextType ────────────────────────────────────────────────────

    [Fact]
    public void FindDbContextType_NullName_AutoDiscoversWhenOne()
    {
        var dll = GetSampleAppDll();
        using var ctx = new ProjectAssemblyContext(dll);

        var type = ctx.FindDbContextType(null);

        Assert.Equal("SampleApp.AppDbContext", type.FullName);
    }

    [Fact]
    public void FindDbContextType_SimpleName_Resolves()
    {
        var dll = GetSampleAppDll();
        using var ctx = new ProjectAssemblyContext(dll);

        var type = ctx.FindDbContextType("AppDbContext");

        Assert.Equal("SampleApp.AppDbContext", type.FullName);
    }

    [Fact]
    public void FindDbContextType_FullyQualifiedName_Resolves()
    {
        var dll = GetSampleAppDll();
        using var ctx = new ProjectAssemblyContext(dll);

        var type = ctx.FindDbContextType("SampleApp.AppDbContext");

        Assert.Equal("SampleApp.AppDbContext", type.FullName);
    }

    [Fact]
    public void FindDbContextType_UnknownName_ThrowsInvalidOperationException()
    {
        var dll = GetSampleAppDll();
        using var ctx = new ProjectAssemblyContext(dll);

        var ex = Assert.Throws<InvalidOperationException>(
            () => ctx.FindDbContextType("NoSuchContext"));

        Assert.Contains("NoSuchContext", ex.Message);
        Assert.Contains("Available", ex.Message);
    }

    // ─── ALC isolation ────────────────────────────────────────────────────────

    [Fact]
    public void IsolationTest_TypeFromALC_IsNotSameRuntimeTypeAsToolDbContext()
    {
        // Phase 1: EF Core is shared between the user's ALC and the host's default
        // ALC so that DbContextOptionsBuilder<T> casts succeed at the bootstrap
        // boundary. As a consequence, AppDbContext IS assignable to the tool's
        // DbContext (both inherit from the same shared runtime type).
        //
        // What remains isolated is the user's own assembly (SampleApp.dll):
        //   - AppDbContext.Assembly lives in the user's ALC, not the default ALC.
        //   - AppDbContext ≠ DbContext (it's a subclass, not the same type).
        //
        // Phase 2 will restore full EF Core isolation once the bootstrap protocol
        // switches to a reflection-only interface.
        var dll = GetSampleAppDll();
        using var ctx = new ProjectAssemblyContext(dll);

        var alcType  = ctx.FindDbContextType("AppDbContext");
        var toolType = typeof(Microsoft.EntityFrameworkCore.DbContext);

        // AppDbContext is a subclass of DbContext — they are NOT the same type.
        Assert.NotEqual(toolType, alcType);

        // Phase 1: EF Core is shared, so AppDbContext IS assignable to the tool's
        // DbContext base class. This is expected and required for bootstrap to work.
        Assert.True(toolType.IsAssignableFrom(alcType),
            "Phase 1: EF Core is shared with the host ALC. " +
            "AppDbContext must be assignable to Microsoft.EntityFrameworkCore.DbContext.");

        // The user's SampleApp assembly itself is still in the user's ALC.
        var userAlc = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(alcType.Assembly);
        Assert.NotSame(System.Runtime.Loader.AssemblyLoadContext.Default, userAlc);
    }

    [Fact]
    public void IsolationTest_AlcName_ContainsAssemblyName()
    {
        // Verify the ALC is distinctly named (aids debugging in memory dumps).
        // We access this indirectly: the type's Assembly's AssemblyLoadContext
        // should not be the default one.
        var dll = GetSampleAppDll();
        using var ctx = new ProjectAssemblyContext(dll);

        var alcType    = ctx.FindDbContextType();
        var typeAlc    = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(alcType.Assembly);
        var defaultAlc = System.Runtime.Loader.AssemblyLoadContext.Default;

        Assert.NotNull(typeAlc);
        Assert.NotSame(defaultAlc, typeAlc);
        Assert.Contains("SampleApp", typeAlc.Name);
    }

    // ─── Dispose / unload ─────────────────────────────────────────────────────

    [Fact]
    public void FindDbContextType_AfterDispose_ThrowsObjectDisposedException()
    {
        var dll = GetSampleAppDll();
        var ctx = new ProjectAssemblyContext(dll);
        ctx.Dispose();

        Assert.Throws<ObjectDisposedException>(() => ctx.FindDbContextType());
    }

    [Fact]
    public void FindDbContextTypes_AfterDispose_ThrowsObjectDisposedException()
    {
        var dll = GetSampleAppDll();
        var ctx = new ProjectAssemblyContext(dll);
        ctx.Dispose();

        Assert.Throws<ObjectDisposedException>(() => ctx.FindDbContextTypes());
    }

    [Fact]
    public void Dispose_SecondCall_IsIdempotent()
    {
        var dll = GetSampleAppDll();
        var ctx = new ProjectAssemblyContext(dll);
        ctx.Dispose();
        ctx.Dispose(); // must not throw
    }

    [Fact]
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Dispose_ThenGC_AlcIsCollected()
    {
        // This test must not inline so the local ProjectAssemblyContext has no
        // JIT-held stack roots keeping the ALC alive after Dispose().
        WeakReference alcRef = CreateAndDisposeContext();

        // Force multiple GC cycles — collectible ALCs can take >1 pass.
        for (int i = 0; i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(alcRef.IsAlive,
            "ALC should have been collected after Dispose() + GC. " +
            "Ensure no strong references remain.");
    }

    // Must be a separate non-inlined method so the JIT doesn't keep
    // the ProjectAssemblyContext (and thus the ALC) alive on the stack.
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndDisposeContext()
    {
        var dll = GetSampleAppDll();
        var ctx = new ProjectAssemblyContext(dll);
        var weakRef = ctx.GetAlcWeakReference();
        ctx.Dispose();
        return weakRef;
    }

    // ─── Factory ──────────────────────────────────────────────────────────────

    [Fact]
    public void Factory_Create_ReturnsContextWithCorrectPath()
    {
        var dll = GetSampleAppDll();
        using var ctx = ProjectAssemblyContextFactory.Create(dll);
        Assert.Equal(dll, ctx.AssemblyPath);
    }

    [Fact]
    public void Factory_IsStale_ReturnsFalseForFreshContext()
    {
        var dll = GetSampleAppDll();
        using var ctx = ProjectAssemblyContextFactory.Create(dll);
        Assert.False(ProjectAssemblyContextFactory.IsStale(ctx));
    }
}
