using System.Diagnostics;
using System.Reflection;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Handlers;
using EFQueryLens.Lsp.HoverPipeline;
using EFQueryLens.Lsp.Parsing;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class HoverRequestCoordinatorTests
{
    [Theory]
    [InlineData(QueryTranslationStatus.Ready)]
    [InlineData(QueryTranslationStatus.DaemonUnavailable)]
    public void IsResolvedForSync_TerminalStatusWithNullContent_ReturnsTrue(
        QueryTranslationStatus status
    )
    {
        var result = new HoverResult(status, Markdown: null, Structured: null);
        Assert.True(HoverFormatting.IsResolvedForSync(result));
    }

    [Fact]
    public void IsResolvedForSync_InQueuePlaceholder_ReturnsTrue()
    {
        Assert.True(HoverFormatting.IsResolvedForSync(HoverFormatting.InQueuePlaceholder()));
    }

    [Fact]
    public async Task HandleStructuredAsync_NoLinqAtCaret_DoesNotReturnInQueueAfterPipelineCompletes()
    {
        const string source = "int x = 1;";
        var sampleFile = Path.Combine(
            FindRepoRoot(),
            "samples",
            "SampleSqliteApp",
            "Application",
            "Customers",
            "CustomerReadService.cs"
        );

        var documentManager = new DocumentManager();
        var warmup = new WarmupHandler(documentManager, new NoOpQueryLensEngine());
        var handler = new HoverHandler(
            documentManager,
            new HoverPreviewService(new NoOpQueryLensEngine()),
            new NoOpQueryLensEngine()
        );
        handler.SetWarmupHandler(warmup);
        handler.ConfigureCachesForTests(
            hoverCacheTtlMs: 15_000,
            inQueueCacheTtlMs: 3_000,
            hoverWaitBudgetMs: 2_000
        );

        var uri = new Uri(sampleFile);
        documentManager.UpdateDocument(uri.ToString(), source);

        var targetAssembly = AssemblyResolver.TryGetTargetAssembly(sampleFile);
        Assert.False(string.IsNullOrWhiteSpace(targetAssembly));
        SeedWarmupCache(warmup, targetAssembly!);

        var request = new TextDocumentPositionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(0, 2),
        };

        var structured = await handler.HandleStructuredAsync(request, CancellationToken.None);

        Assert.True(structured is null || structured.Status is not QueryTranslationStatus.InQueue);
    }

    [Fact]
    public async Task RequestAsync_NonBlockingJoin_ReturnsInQueueWhilePipelineRuns()
    {
        const string source = """
            using System.Linq;

            class Service
            {
                void M(MyDbContext db)
                {
                    var q = db.Orders.Where(o => o.Id > 0).ToList();
                }
            }
            """;
        using var project = TempExecutableProject.Create(source);
        var position = FindPosition(source, "db.Orders");
        var engine = new BlockingQueryLensEngine();
        var chainCache = new DocumentLinqChainCache();
        var coordinator = new HoverRequestCoordinator(
            new HoverPreviewService(engine),
            new QueryRegionResolver(chainCache),
            new HoverResultCache(hoverCacheTtlMs: 15_000, inQueueCacheTtlMs: 3_000),
            chainCache,
            hoverWaitBudgetMs: 5_000,
            foregroundResolveBudgetMs: 75,
            fastProbeEnabled: true,
            hoverQueuedAdaptiveWaitMs: 0,
            structuredQueuedAdaptiveWaitMs: 0
        );
        coordinator.SetAssemblyWarmChecker(_ => true);

        var owner = coordinator.RequestAsync(
            project.SourceFilePath,
            source,
            position.line,
            position.character,
            CancellationToken.None
        );
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var elapsed = Stopwatch.StartNew();
        var joined = await coordinator.RequestAsync(
            project.SourceFilePath,
            source,
            position.line,
            position.character,
            CancellationToken.None,
            nonBlocking: true
        );
        elapsed.Stop();

        Assert.Equal(QueryTranslationStatus.InQueue, joined.Status);
        Assert.True(elapsed.ElapsedMilliseconds < 500);

        engine.Complete();
        var ready = await owner.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(QueryTranslationStatus.Ready, ready.Status);
    }

    [Fact]
    public async Task RequestAsync_WhenForegroundBudgetIsZero_ReturnsInQueueAndConvergesInBackground()
    {
        const string source = """
            using System.Linq;

            class Service
            {
                void M(MyDbContext db)
                {
                    var q = db.Orders.Where(o => o.Id > 0).ToList();
                }
            }
            """;
        using var project = TempExecutableProject.Create(source);
        var position = FindPosition(source, "db.Orders");
        var engine = new BlockingQueryLensEngine();
        var chainCache = new DocumentLinqChainCache();
        var coordinator = new HoverRequestCoordinator(
            new HoverPreviewService(engine),
            new QueryRegionResolver(chainCache),
            new HoverResultCache(hoverCacheTtlMs: 15_000, inQueueCacheTtlMs: 3_000),
            chainCache,
            hoverWaitBudgetMs: 0,
            foregroundResolveBudgetMs: 0,
            fastProbeEnabled: true,
            hoverQueuedAdaptiveWaitMs: 0,
            structuredQueuedAdaptiveWaitMs: 0
        );

        var elapsed = Stopwatch.StartNew();
        var first = await coordinator.RequestAsync(
            project.SourceFilePath,
            source,
            position.line,
            position.character,
            CancellationToken.None
        );
        elapsed.Stop();

        Assert.Equal(QueryTranslationStatus.InQueue, first.Status);
        Assert.True(elapsed.ElapsedMilliseconds < 2_000);

        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        engine.Complete();

        await WaitUntilReadyAsync(
            coordinator,
            project.SourceFilePath,
            source,
            position.line,
            position.character
        );
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "samples", "SampleSqliteApp")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate repository root for hover coordinator tests."
        );
    }

    private static void SeedWarmupCache(WarmupHandler warmup, string assemblyPath)
    {
        var field = typeof(WarmupHandler).GetField(
            "_warmCache",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var cacheType = field.FieldType;
        var cache = field.GetValue(warmup)!;
        var cachedWarmupType = typeof(WarmupHandler).GetNestedType(
            "CachedWarmup",
            BindingFlags.NonPublic
        )!;
        var cached = Activator.CreateInstance(
            cachedWarmupType,
            DateTime.UtcNow.AddMinutes(5).Ticks,
            true,
            "ready"
        )!;
        cacheType.GetMethod("set_Item")!.Invoke(cache, [assemblyPath, cached]);
    }

    [Fact]
    public void RegionResolver_ConcurrentResolveOnSameStatement_DedupesInflightWork()
    {
        const string source = """
            var ids = await dbContext.Orders
                .Where(o => o.Id > 0)
                .ToListAsync();
            """;
        var resolver = new QueryRegionResolver(new DocumentLinqChainCache());
        var first = FindPosition(source, "dbContext");
        var second = FindPosition(source, "Where");

        var keyA = QueryRegionResolver.BuildRegionInflightKey(
            "file.cs",
            source,
            first.line,
            first.character
        );
        var keyB = QueryRegionResolver.BuildRegionInflightKey(
            "file.cs",
            source,
            second.line,
            second.character
        );
        Assert.Equal(keyA, keyB);

        var taskA = Task.Run(() =>
            resolver.TryResolve("file.cs", source, first.line, first.character)
        );
        var taskB = Task.Run(() =>
            resolver.TryResolve("file.cs", source, second.line, second.character)
        );
        Task.WaitAll(taskA, taskB);

        Assert.True(taskA.Result.Found);
        Assert.True(taskB.Result.Found);
        Assert.Equal(taskA.Result.Region!.SemanticKey, taskB.Result.Region!.SemanticKey);
    }

    [Fact]
    public void DocumentChainCache_InvalidatesOnDocumentChange()
    {
        var handler = new HoverHandler(
            new DocumentManager(),
            new HoverPreviewService(new NoOpQueryLensEngine())
        );
        const string path = "file.cs";
        const string source = "var q = db.Orders.ToList();";

        _ = handler.RegionResolver.TryResolve(path, source, 0, 8);
        handler.OnDocumentChanged(path);
        var chainsAfter = handler.RegionResolver;
        var result = chainsAfter.TryResolve(path, source, 0, 8);
        Assert.True(result.Found);
    }

    [Fact]
    public void OnDocumentChanged_ClearsRegisteredSpanIndex()
    {
        var handler = new HoverHandler(
            new DocumentManager(),
            new HoverPreviewService(new NoOpQueryLensEngine())
        );
        const string path = "file.cs";
        const string source = "var q = db.Orders.Where(o => o.Id == 1).ToList();";

        _ = handler.RegionResolver.TryResolve(path, source, 0, 12);
        Assert.True(
            handler.RegionResolver.TryGetSemanticKeyByPosition(path, source, 0, 12, out var keyBefore)
        );
        Assert.False(string.IsNullOrWhiteSpace(keyBefore));

        handler.OnDocumentChanged(path);

        Assert.False(
            handler.RegionResolver.TryGetSemanticKeyByPosition(path, source, 0, 12, out _)
        );
    }

    private static (int line, int character) FindPosition(string source, string marker)
    {
        var index = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0);
        var line = 0;
        var character = 0;
        for (var i = 0; i < index; i++)
        {
            if (source[i] == '\n')
            {
                line++;
                character = 0;
            }
            else
            {
                character++;
            }
        }

        return (line, character);
    }

    private static async Task WaitUntilReadyAsync(
        HoverRequestCoordinator coordinator,
        string filePath,
        string source,
        int line,
        int character
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var result = await coordinator.RequestAsync(
                filePath,
                source,
                line,
                character,
                CancellationToken.None,
                nonBlocking: true
            );
            if (result.Status == QueryTranslationStatus.Ready)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Hover result did not converge to Ready.");
    }

    private sealed class NoOpQueryLensEngine : IQueryLensEngine
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task InvalidateAssemblyCachesAsync(CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<QueryTranslationResult> TranslateAsync(
            TranslationRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(new QueryTranslationResult());

        public Task<ModelSnapshot> InspectModelAsync(
            ModelInspectionRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(new ModelSnapshot { DbContextType = string.Empty });
    }

    private sealed class BlockingQueryLensEngine : IQueryLensEngine
    {
        private readonly TaskCompletionSource<QueryTranslationResult> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task InvalidateAssemblyCachesAsync(CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<QueryTranslationResult> TranslateAsync(
            TranslationRequest request,
            CancellationToken ct = default
        )
        {
            Started.TrySetResult();
            return completion.Task;
        }

        public Task<ModelSnapshot> InspectModelAsync(
            ModelInspectionRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(new ModelSnapshot { DbContextType = string.Empty });

        public void Complete()
        {
            completion.TrySetResult(
                new QueryTranslationResult
                {
                    Success = true,
                    Commands = [new QuerySqlCommand { Sql = "SELECT 1;" }],
                    Metadata = new TranslationMetadata
                    {
                        DbContextType = "MyDbContext",
                        EfCoreVersion = "10.0.0",
                        ProviderName = "Microsoft.EntityFrameworkCore.Sqlite",
                        TranslationTime = TimeSpan.FromMilliseconds(1),
                    },
                }
            );
        }
    }

    private sealed class TempExecutableProject : IDisposable
    {
        private TempExecutableProject(string root, string sourceFilePath)
        {
            Root = root;
            SourceFilePath = sourceFilePath;
        }

        public string Root { get; }
        public string SourceFilePath { get; }

        public static TempExecutableProject Create(string source)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "efql-coordinator-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(root);
            var sourceFile = Path.Combine(root, "Service.cs");
            var outputDir = Path.Combine(root, "bin", "Debug", "net10.0");
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(
                Path.Combine(root, "TestApp.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """
            );
            File.WriteAllText(sourceFile, source);
            File.WriteAllBytes(Path.Combine(outputDir, "TestApp.dll"), [0]);

            return new TempExecutableProject(root, sourceFile);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp test files.
            }
        }
    }
}
