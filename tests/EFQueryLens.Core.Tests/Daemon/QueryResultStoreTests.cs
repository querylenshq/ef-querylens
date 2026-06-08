using EFQueryLens.Core.Contracts;
using EFQueryLens.Daemon;

namespace EFQueryLens.Core.Tests.Daemon;

/// <summary>
/// Verifies the durable SQLite-backed query result store:
///   - successful translations round-trip (and survive reopening the database),
///   - a rebuild (new assembly fingerprint) prunes stale rows for that assembly,
///   - unrelated assemblies are not pruned,
///   - Clear() empties the store.
/// </summary>
public sealed class QueryResultStoreTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"querylens-store-test-{Guid.NewGuid():N}.db");

    private static QueryTranslationResult Result(string sql) => new()
    {
        Success = true,
        Sql = sql,
        Commands = [new QuerySqlCommand { Sql = sql }],
    };

    private QueryResultStore Open() =>
        QueryResultStore.TryOpen(_dbPath, QueryLensJsonOptions.Create())
        ?? throw new InvalidOperationException("Expected the store to open.");

    [Fact]
    public void Set_ThenTryGet_RoundTripsResult()
    {
        using var store = Open();
        store.Set("key-1", "App.dll", "fp-1", Result("SELECT 1"));

        var fetched = store.TryGet("key-1");

        Assert.NotNull(fetched);
        Assert.True(fetched!.Success);
        Assert.Equal("SELECT 1", fetched.Sql);
    }

    [Fact]
    public void TryGet_UnknownKey_ReturnsNull()
    {
        using var store = Open();
        Assert.Null(store.TryGet("does-not-exist"));
    }

    [Fact]
    public void Result_SurvivesReopeningTheDatabase()
    {
        using (var store = Open())
        {
            store.Set("key-persist", "App.dll", "fp-1", Result("SELECT persisted"));
        }

        // A fresh store over the same file simulates a daemon restart / idle-shutdown recovery.
        using var reopened = Open();
        var fetched = reopened.TryGet("key-persist");

        Assert.NotNull(fetched);
        Assert.Equal("SELECT persisted", fetched!.Sql);
    }

    [Fact]
    public void Set_WithNewFingerprint_PrunesStaleRowsForSameAssembly()
    {
        using var store = Open();

        // Two queries against the same assembly at fingerprint fp-1.
        store.Set("old-a", "App.dll", "fp-1", Result("SELECT a"));
        store.Set("old-b", "App.dll", "fp-1", Result("SELECT b"));

        // The assembly is rebuilt → new fingerprint fp-2. Writing any fp-2 row prunes fp-1 rows.
        store.Set("new-a", "App.dll", "fp-2", Result("SELECT a2"));

        Assert.Null(store.TryGet("old-a"));
        Assert.Null(store.TryGet("old-b"));
        Assert.NotNull(store.TryGet("new-a"));
    }

    [Fact]
    public void Set_WithNewFingerprint_DoesNotPruneOtherAssemblies()
    {
        using var store = Open();
        store.Set("other", "Other.dll", "ofp-1", Result("SELECT other"));
        store.Set("app", "App.dll", "fp-2", Result("SELECT app"));

        // Rebuilding App.dll must not evict results belonging to Other.dll.
        Assert.NotNull(store.TryGet("other"));
        Assert.NotNull(store.TryGet("app"));
    }

    [Fact]
    public void Set_EvictsOldestRowsWhenMaxRowsExceeded()
    {
        var previous = Environment.GetEnvironmentVariable("QUERYLENS_CACHE_MAX_ROWS");
        try
        {
            Environment.SetEnvironmentVariable("QUERYLENS_CACHE_MAX_ROWS", "2");
            using var store = Open();
            store.Set("k1", "App.dll", "fp-1", Result("SELECT 1"));
            Thread.Sleep(2);
            store.Set("k2", "App.dll", "fp-1", Result("SELECT 2"));
            store.Set("k3", "App.dll", "fp-1", Result("SELECT 3"));

            Assert.Null(store.TryGet("k1"));
            Assert.NotNull(store.TryGet("k2"));
            Assert.NotNull(store.TryGet("k3"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUERYLENS_CACHE_MAX_ROWS", previous);
        }
    }

    [Fact]
    public void Clear_RemovesAllRows()
    {
        using var store = Open();
        store.Set("k1", "App.dll", "fp-1", Result("SELECT 1"));
        store.Set("k2", "App.dll", "fp-1", Result("SELECT 2"));

        store.Clear();

        Assert.Null(store.TryGet("k1"));
        Assert.Null(store.TryGet("k2"));
    }

    public void Dispose()
    {
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
        }
    }
}
