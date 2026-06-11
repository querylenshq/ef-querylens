using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Engine;
using EFQueryLens.Lsp.Handlers;
using EFQueryLens.Lsp.Services;

namespace EFQueryLens.Core.Tests.Lsp;

public class AssemblyChangeTrackerTests
{
    [Fact]
    public async Task CheckAndInvalidateIfChanged_FingerprintChange_InvalidatesHoverAndDaemonCaches()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), "ql-asm-tracker-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(sourceDir);

        var csprojPath = Path.Combine(sourceDir, "TrackerApp.csproj");
        File.WriteAllText(csprojPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var sourceFile = Path.Combine(sourceDir, "Program.cs");
        File.WriteAllText(sourceFile, "class Program { static void Main() {} }");

        var binDir = Path.Combine(sourceDir, "bin", "Debug", "net8.0");
        Directory.CreateDirectory(binDir);
        var dllPath = Path.Combine(binDir, "TrackerApp.dll");
        File.WriteAllBytes(dllPath, [0x4D, 0x5A]);

        var engine = new TrackingEngineControl();
        var handler = new HoverHandler(new DocumentManager(), new HoverPreviewService(engine), engine);
        var tracker = new AssemblyChangeTracker(handler);

        try
        {
            tracker.CheckAndInvalidateIfChanged(sourceFile);
            Assert.Equal(0, engine.InvalidateCount);

            File.WriteAllBytes(dllPath, [0x4D, 0x5A, 0x01]);
            tracker.CheckAndInvalidateIfChanged(sourceFile);

            await engine.InvalidateAwaiter.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, engine.InvalidateCount);
        }
        finally
        {
            try { Directory.Delete(sourceDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task CheckOnSave_FingerprintChange_InvalidatesHoverAndDaemonCaches()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), "ql-asm-tracker-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(sourceDir);

        var csprojPath = Path.Combine(sourceDir, "TrackerApp.csproj");
        File.WriteAllText(csprojPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var sourceFile = Path.Combine(sourceDir, "Program.cs");
        File.WriteAllText(sourceFile, "class Program { static void Main() {} }");

        var binDir = Path.Combine(sourceDir, "bin", "Debug", "net8.0");
        Directory.CreateDirectory(binDir);
        var dllPath = Path.Combine(binDir, "TrackerApp.dll");
        File.WriteAllBytes(dllPath, [0x4D, 0x5A]);

        var engine = new TrackingEngineControl();
        var handler = new HoverHandler(new DocumentManager(), new HoverPreviewService(engine), engine);
        var tracker = new AssemblyChangeTracker(handler);

        try
        {
            tracker.CheckOnSave(sourceFile);
            Assert.Equal(0, engine.InvalidateCount);

            File.WriteAllBytes(dllPath, [0x4D, 0x5A, 0x01]);
            tracker.CheckOnSave(sourceFile);

            await engine.InvalidateAwaiter.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, engine.InvalidateCount);
        }
        finally
        {
            try { Directory.Delete(sourceDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private sealed class TrackingEngineControl : IQueryLensEngine, IEngineControl
    {
        private readonly TaskCompletionSource _invalidateAwaiter = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int InvalidateCount { get; private set; }

        public Task InvalidateAwaiter => _invalidateAwaiter.Task;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task InvalidateAssemblyCachesAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<QueryTranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct = default)
            => Task.FromResult(new QueryTranslationResult());

        public Task<ModelSnapshot> InspectModelAsync(ModelInspectionRequest request, CancellationToken ct = default)
            => Task.FromResult(new ModelSnapshot { DbContextType = string.Empty });

        public Task PingAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task RestartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task InvalidateCacheAsync(CancellationToken ct = default)
        {
            InvalidateCount++;
            _invalidateAwaiter.TrySetResult();
            return Task.CompletedTask;
        }

        public Task WarmTranslateAsync(TranslationRequest request, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
