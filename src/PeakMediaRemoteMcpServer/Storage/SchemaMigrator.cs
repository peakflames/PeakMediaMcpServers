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
    public static readonly IReadOnlyList<(int Version, string Sql)> Migrations = [];

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
