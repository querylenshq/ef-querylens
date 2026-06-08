using System.Text.Json;
using EFQueryLens.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace EFQueryLens.Daemon;

/// <summary>
/// Durable, content-addressed store for successful query translations, backed by a per-workspace
/// SQLite database under <c>%LOCALAPPDATA%/EFQueryLens/Cache/&lt;workspaceHash&gt;.db</c>.
///
/// This is the persistence layer that lets analysed SQL survive the daemon's idle-shutdown and an
/// IDE restart — so the same unchanged query is never re-translated just because a process was
/// recycled. The in-memory <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> remains
/// the hot layer in front of it.
///
/// Rows are keyed by the same content hash the daemon uses for its in-memory cache (which now
/// embeds the assembly fingerprint), so a rebuild produces new keys automatically. On every write
/// we additionally delete rows for the same assembly whose fingerprint no longer matches, so the
/// database does not accumulate garbage across rebuilds.
///
/// All access is serialised through a single connection + lock: query translation is the expensive
/// operation, the SQLite read/write is negligible by comparison, so contention is not a concern.
/// </summary>
internal sealed class QueryResultStore : IDisposable
{
    private const int DefaultMaxRows = 5_000;

    private readonly SqliteConnection _connection;
    private readonly JsonSerializerOptions _json;
    private readonly int _maxRows;
    private readonly object _gate = new();

    private QueryResultStore(SqliteConnection connection, JsonSerializerOptions json, int maxRows)
    {
        _connection = connection;
        _json = json;
        _maxRows = maxRows;
    }

    /// <summary>
    /// Opens (creating if needed) the SQLite store at <paramref name="dbPath"/>. Returns
    /// <c>null</c> if the store cannot be opened for any reason — callers degrade gracefully to
    /// the in-memory cache only.
    /// </summary>
    public static QueryResultStore? TryOpen(string dbPath, JsonSerializerOptions json)
    {
        try
        {
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
                pragma.ExecuteNonQuery();
            }

            using (var create = connection.CreateCommand())
            {
                create.CommandText =
                    """
                    CREATE TABLE IF NOT EXISTS query_results (
                        cache_key            TEXT PRIMARY KEY,
                        assembly_path        TEXT NOT NULL,
                        assembly_fingerprint TEXT NOT NULL,
                        result_json          TEXT NOT NULL,
                        created_utc          INTEGER NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_query_results_assembly
                        ON query_results(assembly_path);
                    """;
                create.ExecuteNonQuery();
            }

            return new QueryResultStore(connection, json, ReadMaxRows());
        }
        catch
        {
            return null;
        }
    }

    private static int ReadMaxRows()
    {
        var raw = Environment.GetEnvironmentVariable("QUERYLENS_CACHE_MAX_ROWS");
        if (int.TryParse(raw, out var parsed) && parsed >= 0)
        {
            return parsed;
        }

        return DefaultMaxRows;
    }

    /// <summary>Returns the persisted translation for <paramref name="cacheKey"/>, or null.</summary>
    public QueryTranslationResult? TryGet(string cacheKey)
    {
        lock (_gate)
        {
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT result_json FROM query_results WHERE cache_key = $k LIMIT 1;";
                cmd.Parameters.AddWithValue("$k", cacheKey);

                if (cmd.ExecuteScalar() is not string json || json.Length == 0)
                {
                    return null;
                }

                return JsonSerializer.Deserialize<QueryTranslationResult>(json, _json);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Persists a successful translation. Also deletes any rows for the same assembly whose
    /// fingerprint differs from <paramref name="assemblyFingerprint"/>, pruning results that a
    /// rebuild has made obsolete.
    /// </summary>
    public void Set(string cacheKey, string assemblyPath, string assemblyFingerprint, QueryTranslationResult result)
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(result, _json);
        }
        catch
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                using var tx = _connection.BeginTransaction();

                using (var prune = _connection.CreateCommand())
                {
                    prune.Transaction = tx;
                    prune.CommandText =
                        "DELETE FROM query_results WHERE assembly_path = $p AND assembly_fingerprint <> $f;";
                    prune.Parameters.AddWithValue("$p", assemblyPath);
                    prune.Parameters.AddWithValue("$f", assemblyFingerprint);
                    prune.ExecuteNonQuery();
                }

                using (var upsert = _connection.CreateCommand())
                {
                    upsert.Transaction = tx;
                    upsert.CommandText =
                        """
                        INSERT INTO query_results (cache_key, assembly_path, assembly_fingerprint, result_json, created_utc)
                        VALUES ($k, $p, $f, $j, $t)
                        ON CONFLICT(cache_key) DO UPDATE SET
                            assembly_fingerprint = excluded.assembly_fingerprint,
                            result_json          = excluded.result_json,
                            created_utc          = excluded.created_utc;
                        """;
                    upsert.Parameters.AddWithValue("$k", cacheKey);
                    upsert.Parameters.AddWithValue("$p", assemblyPath);
                    upsert.Parameters.AddWithValue("$f", assemblyFingerprint);
                    upsert.Parameters.AddWithValue("$j", json);
                    upsert.Parameters.AddWithValue("$t", DateTime.UtcNow.Ticks);
                    upsert.ExecuteNonQuery();
                }

                EvictOldestIfNeeded(tx);

                tx.Commit();
            }
            catch
            {
                // Best-effort persistence — a failed write must never break translation.
            }
        }
    }

    private void EvictOldestIfNeeded(SqliteTransaction tx)
    {
        if (_maxRows <= 0)
        {
            return;
        }

        using var countCmd = _connection.CreateCommand();
        countCmd.Transaction = tx;
        countCmd.CommandText = "SELECT COUNT(*) FROM query_results;";
        var count = Convert.ToInt64(countCmd.ExecuteScalar());
        if (count <= _maxRows)
        {
            return;
        }

        var toDelete = count - _maxRows;
        using var deleteCmd = _connection.CreateCommand();
        deleteCmd.Transaction = tx;
        deleteCmd.CommandText =
            """
            DELETE FROM query_results
            WHERE cache_key IN (
                SELECT cache_key
                FROM query_results
                ORDER BY created_utc ASC
                LIMIT $n
            );
            """;
        deleteCmd.Parameters.AddWithValue("$n", toDelete);
        deleteCmd.ExecuteNonQuery();
    }

    /// <summary>Removes all persisted results. Invoked on explicit cache invalidation.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM query_results;";
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }
    }
}
