using System.Reflection;
using EFQueryLens.Core.Contracts;
using EFQueryLens.Lsp;
using EFQueryLens.Lsp.Handlers;
using EFQueryLens.Lsp.HoverPipeline;
using EFQueryLens.Lsp.Parsing;
using EFQueryLens.Lsp.Services;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace EFQueryLens.Core.Tests.Lsp;

public sealed class HoverSqlReadyNotificationTests
{
    private const string SourceWithQuery = "var q = db.Orders.Where(o => o.Id == 1).ToList();";

    [Fact]
    public async Task HandleAsync_InQueueThenBackgroundSuccess_NotifiesOnce()
    {
        var notifications = new CapturingSqlReadyNotifier();
        var (handler, request, sampleFile) = Setup(
            waitBudgetMs: 0,
            engine: new DelayedTranslationEngine(delayMs: 400, success: true));

        handler.SetSqlReadyNotifierForTests(notifications);

        var hover = await handler.HandleAsync(request, CancellationToken.None);
        Assert.NotNull(hover);
        Assert.Contains("translating", Markdown(hover!), StringComparison.OrdinalIgnoreCase);

        var notified = await WaitForNotificationsAsync(notifications, expectedCount: 1, timeoutMs: 5_000);
        Assert.True(notified);

        var payload = Assert.Single(notifications.Notifications);
        Assert.Equal(Path.GetFileName(sampleFile), payload.FileName);
        Assert.True(payload.CommandCount > 0);
        Assert.False(string.IsNullOrWhiteSpace(payload.FileUri));
    }

    [Fact]
    public async Task HandleAsync_SyncReady_DoesNotNotify()
    {
        var notifications = new CapturingSqlReadyNotifier();
        var (handler, request, sampleFile) = Setup(
            waitBudgetMs: 5_000,
            engine: new DelayedTranslationEngine(delayMs: 50, success: true),
            seedWarmAssembly: true);

        handler.SetSqlReadyNotifierForTests(notifications);

        var hover = await handler.HandleAsync(request, CancellationToken.None);
        Assert.NotNull(hover);
        Assert.DoesNotContain("translating", Markdown(hover!), StringComparison.OrdinalIgnoreCase);

        await Task.Delay(300);
        Assert.Empty(notifications.Notifications);
    }

    [Fact]
    public async Task HandleAsync_InQueueThenBackgroundFailure_DoesNotNotify()
    {
        var notifications = new CapturingSqlReadyNotifier();
        var (handler, request, _) = Setup(
            waitBudgetMs: 0,
            engine: new DelayedTranslationEngine(delayMs: 200, success: false, errorMessage: "boom"));

        handler.SetSqlReadyNotifierForTests(notifications);

        var hover = await handler.HandleAsync(request, CancellationToken.None);
        Assert.NotNull(hover);
        Assert.Contains("translating", Markdown(hover!), StringComparison.OrdinalIgnoreCase);

        await Task.Delay(1_500);
        Assert.Empty(notifications.Notifications);
    }

    [Fact]
    public async Task CapturingSqlReadyNotifier_WhenDisabled_DoesNotRecord()
    {
        var notifier = new CapturingSqlReadyNotifier { Enabled = false };
        await notifier.NotifyAsync(new SqlReadyNotification("file:///a.cs", 1, 2, "a.cs", 1));
        Assert.Empty(notifier.Notifications);
    }

    private static (HoverHandler handler, TextDocumentPositionParams request, string sampleFile) Setup(
        int waitBudgetMs,
        IQueryLensEngine engine,
        bool seedWarmAssembly = false)
    {
        var sampleFile = Path.Combine(
            FindRepoRoot(),
            "samples",
            "SampleSqliteApp",
            "Application",
            "Customers",
            "CustomerReadService.cs");

        var documentManager = new DocumentManager();
        var warmup = new WarmupHandler(documentManager, engine);
        var handler = new HoverHandler(documentManager, new HoverPreviewService(engine), engine);
        handler.SetWarmupHandler(warmup);
        handler.ConfigureCachesForTests(hoverCacheTtlMs: 15_000, inQueueCacheTtlMs: 3_000, hoverWaitBudgetMs: waitBudgetMs);

        if (seedWarmAssembly)
        {
            var targetAssembly = AssemblyResolver.TryGetTargetAssembly(sampleFile);
            if (!string.IsNullOrWhiteSpace(targetAssembly))
            {
                SeedWarmupCache(warmup, targetAssembly);
            }
        }

        var uri = new Uri(sampleFile);
        documentManager.UpdateDocument(uri.ToString(), SourceWithQuery);

        var request = new TextDocumentPositionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(0, 12),
        };

        return (handler, request, sampleFile);
    }

    private static async Task<bool> WaitForNotificationsAsync(
        CapturingSqlReadyNotifier notifier,
        int expectedCount,
        int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (notifier.Notifications.Count >= expectedCount)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return notifier.Notifications.Count >= expectedCount;
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

        throw new InvalidOperationException("Could not locate repository root for SQL-ready notification tests.");
    }

    private static string Markdown(Hover hover)
        => ((MarkupContent)hover.Contents.Value!).Value;

    private static void SeedWarmupCache(WarmupHandler warmup, string assemblyPath)
    {
        var field = typeof(WarmupHandler).GetField("_warmCache", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var cacheType = field.FieldType;
        var cache = field.GetValue(warmup)!;
        var cachedWarmupType = typeof(WarmupHandler).GetNestedType("CachedWarmup", BindingFlags.NonPublic)!;
        var cached = Activator.CreateInstance(
            cachedWarmupType,
            DateTime.UtcNow.AddMinutes(5).Ticks,
            true,
            "ready")!;
        cacheType.GetMethod("set_Item")!.Invoke(cache, [assemblyPath, cached]);
    }

    private sealed class CapturingSqlReadyNotifier : IHoverReadyNotifier
    {
        public bool Enabled { get; init; } = true;

        public List<SqlReadyNotification> Notifications { get; } = [];

        public bool IsEnabled => Enabled;

        public ValueTask NotifyAsync(SqlReadyNotification notification, CancellationToken cancellationToken = default)
        {
            if (!IsEnabled)
            {
                return ValueTask.CompletedTask;
            }

            Notifications.Add(notification);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DelayedTranslationEngine : IQueryLensEngine
    {
        private readonly int _delayMs;
        private readonly bool _success;
        private readonly string? _errorMessage;

        public DelayedTranslationEngine(int delayMs, bool success, string? errorMessage = null)
        {
            _delayMs = delayMs;
            _success = success;
            _errorMessage = errorMessage;
        }

        public async Task<QueryTranslationResult> TranslateAsync(
            TranslationRequest request,
            CancellationToken ct = default)
        {
            await Task.Delay(_delayMs, ct);
            if (!_success)
            {
                return new QueryTranslationResult
                {
                    Success = false,
                    ErrorMessage = _errorMessage ?? "Translation failed.",
                    Metadata = new TranslationMetadata
                    {
                        ProviderName = "test",
                        DbContextType = "TestContext",
                        EfCoreVersion = "9.0.0",
                        TranslationTime = TimeSpan.FromMilliseconds(_delayMs),
                    },
                };
            }

            return new QueryTranslationResult
            {
                Success = true,
                Sql = "SELECT 1",
                Commands = [new QuerySqlCommand { Sql = "SELECT 1" }],
                Metadata = new TranslationMetadata
                {
                    ProviderName = "Microsoft.EntityFrameworkCore.Sqlite",
                    DbContextType = "SampleSqliteApp.Infrastructure.Persistence.SqliteAppDbContext",
                    EfCoreVersion = "9.0.0",
                    TranslationTime = TimeSpan.FromMilliseconds(_delayMs),
                },
            };
        }

        public Task<ModelSnapshot> InspectModelAsync(ModelInspectionRequest request, CancellationToken ct = default)
            => Task.FromResult(new ModelSnapshot { DbContextType = "TestContext" });

        public Task InvalidateAssemblyCachesAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
