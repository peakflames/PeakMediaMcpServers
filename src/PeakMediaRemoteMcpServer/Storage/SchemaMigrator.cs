namespace PeakMediaRemoteMcpServer.Storage;

/// <summary>
/// Brings the database schema up to date at startup: creates <c>schema_version</c> and applies each
/// migration in <see cref="Migrations"/> whose number is above the recorded version. Later epics
/// append migrations (usage log, quotas, audio cache); every table that holds a caller's records
/// carries the caller's Okta subject as its owner column.
/// </summary>
public sealed class SchemaMigrator
{
    /// <summary>Ordered migrations: (version, SQL). Append only; never edit a shipped entry.</summary>
    public static readonly IReadOnlyList<(int Version, string Sql)> Migrations =
    [
        // 1 — sign-in (Epic 7BASFbp): key/value metadata (the token-signing key), the users table whose
        // subject is the owner key every later table carries, and the broker's OAuth state. Times are
        // Unix seconds (UTC). Secrets the gateway only verifies are stored as SHA-256 hashes.
        (1, """
            CREATE TABLE app_meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE users (
                subject        TEXT PRIMARY KEY,
                email          TEXT NOT NULL,
                is_admin       INTEGER NOT NULL DEFAULT 0,
                first_seen_utc INTEGER NOT NULL,
                last_seen_utc  INTEGER NOT NULL
            );
            CREATE TABLE oauth_clients (
                client_id          TEXT PRIMARY KEY,
                client_secret_hash TEXT NOT NULL,
                redirect_uris_json TEXT NOT NULL,
                client_name        TEXT,
                created_utc        INTEGER NOT NULL
            );
            CREATE TABLE oauth_transactions (
                txn_id                 TEXT PRIMARY KEY,
                client_id              TEXT NOT NULL,
                client_redirect_uri    TEXT NOT NULL,
                client_state           TEXT NOT NULL,
                client_code_challenge  TEXT NOT NULL,
                upstream_code_verifier TEXT NOT NULL,
                nonce                  TEXT NOT NULL,
                consent_token_hash     TEXT NOT NULL,
                created_utc            INTEGER NOT NULL,
                expires_utc            INTEGER NOT NULL
            );
            CREATE TABLE oauth_codes (
                code_hash             TEXT PRIMARY KEY,
                subject               TEXT NOT NULL REFERENCES users(subject) ON DELETE CASCADE,
                client_id             TEXT NOT NULL,
                client_redirect_uri   TEXT NOT NULL,
                client_code_challenge TEXT NOT NULL,
                expires_utc           INTEGER NOT NULL
            );
            CREATE TABLE oauth_refresh_tokens (
                token_hash  TEXT PRIMARY KEY,
                subject     TEXT NOT NULL REFERENCES users(subject) ON DELETE CASCADE,
                client_id   TEXT NOT NULL,
                expires_utc INTEGER NOT NULL
            );
            CREATE INDEX ix_oauth_codes_subject ON oauth_codes(subject);
            CREATE INDEX ix_oauth_refresh_tokens_subject ON oauth_refresh_tokens(subject);
            """),

        // 2 — narration (Epic 8po1tRg): the shared audio cache index (the WAV files live in
        // Storage:AudioDirectory) and short-lived download tokens. The cache is shared by design, so
        // it is read by key and records the subject who created each clip; a download token is owned
        // by the caller it was issued to and is stored only as its SHA-256 hash.
        (2, """
            CREATE TABLE audio_cache (
                cache_key          TEXT PRIMARY KEY,
                size_bytes         INTEGER NOT NULL,
                duration_ms        INTEGER NOT NULL,
                created_by_subject TEXT NOT NULL,
                created_utc        INTEGER NOT NULL,
                last_used_utc      INTEGER NOT NULL
            );
            CREATE TABLE clip_downloads (
                token_hash  TEXT PRIMARY KEY,
                cache_key   TEXT NOT NULL REFERENCES audio_cache(cache_key) ON DELETE CASCADE,
                subject     TEXT NOT NULL,
                created_utc INTEGER NOT NULL,
                expires_utc INTEGER NOT NULL
            );
            CREATE INDEX ix_audio_cache_created_by_subject ON audio_cache(created_by_subject);
            CREATE INDEX ix_clip_downloads_subject ON clip_downloads(subject);
            CREATE INDEX ix_clip_downloads_expires ON clip_downloads(expires_utc);
            """),

        // 3 — quotas and usage (Epic EYGrros): one usage_log row per clip of a call (a synthesized clip
        // at its cost, a cache hit at 0) and per-user monthly quota overrides. Both are owned by
        // `subject` and read or written only through AccessRule (the caller's own rows, or an admin).
        // Money is integer nano-dollars so sums are exact; times are Unix milliseconds (UTC). The
        // usage log never holds narration text — only its character count and cache key.
        (3, """
            CREATE TABLE usage_log (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                subject        TEXT NOT NULL,
                at_utc         INTEGER NOT NULL,
                model          TEXT NOT NULL,
                voice          TEXT NOT NULL,
                characters     INTEGER NOT NULL,
                cost_nanousd   INTEGER NOT NULL,
                cache_key      TEXT NOT NULL,
                cache_hit      INTEGER NOT NULL
            );
            CREATE INDEX ix_usage_log_subject_at ON usage_log(subject, at_utc);
            CREATE TABLE quota_overrides (
                subject         TEXT PRIMARY KEY,
                monthly_nanousd INTEGER NOT NULL,
                set_by_subject  TEXT NOT NULL,
                set_at_utc      INTEGER NOT NULL
            );
            """),

        // 4 — cache overview (Epic G8jMzOz): one row per clip the eviction pass removed, so the admin
        // Cache screen can count "evicted this month". Each row is owned by `created_by_subject` —
        // copied from the evicted clip's audio_cache row, the subject who created it — and is read
        // only through the admin-gated CacheStatsReader. Times are Unix milliseconds (UTC); counting
        // starts when this migration is applied.
        (4, """
            CREATE TABLE cache_evictions (
                id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                cache_key          TEXT NOT NULL,
                created_by_subject TEXT NOT NULL,
                at_utc             INTEGER NOT NULL,
                size_bytes         INTEGER NOT NULL
            );
            CREATE INDEX ix_cache_evictions_at ON cache_evictions(at_utc);
            CREATE INDEX ix_cache_evictions_created_by_subject ON cache_evictions(created_by_subject);
            """),
    ];

    private readonly SqliteConnectionFactory _connections;

    public SchemaMigrator(SqliteConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<Result<int>> MigrateAsync(CancellationToken cancellationToken = default)
    {
        var opened = await _connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return opened.ToResult<int>();
        }

        await using var connection = opened.Value;
        await using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL, applied_utc TEXT NOT NULL);";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        var current = await ReadVersionAsync(connection, cancellationToken);
        foreach (var (version, sql) in Migrations.Where(m => m.Version > current).OrderBy(m => m.Version))
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using (var migrate = connection.CreateCommand())
            {
                migrate.Transaction = transaction;
                migrate.CommandText = sql;
                await migrate.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var record = connection.CreateCommand())
            {
                record.Transaction = transaction;
                record.CommandText = "INSERT INTO schema_version (version, applied_utc) VALUES ($version, $applied);";
                record.Parameters.AddWithValue("$version", version);
                record.Parameters.AddWithValue("$applied", DateTimeOffset.UtcNow.ToString("O"));
                await record.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            current = version;
        }

        return Result.Ok(current);
    }

    private static async Task<int> ReadVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
        var value = await read.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
