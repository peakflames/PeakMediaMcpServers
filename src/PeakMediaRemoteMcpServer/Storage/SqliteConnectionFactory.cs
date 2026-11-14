namespace PeakMediaRemoteMcpServer.Storage;

/// <summary>Opens connections to the gateway's SQLite database (WAL mode), honoring the test fault switch.</summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string databasePath, StorageFaultSwitch faultSwitch)
    {
        DatabasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        FaultSwitch = faultSwitch;
    }

    public string DatabasePath { get; }

    /// <summary>
    /// The test fault switch. Set once from the environment at startup; in-process end-to-end tests
    /// replace it (internal, never reachable from a request) to clear a fault while the page is open.
    /// </summary>
    internal StorageFaultSwitch FaultSwitch { get; set; }

    public async Task<Result<SqliteConnection>> OpenAsync(CancellationToken cancellationToken = default)
    {
        var faultSwitch = FaultSwitch;
        if (faultSwitch.Delay > TimeSpan.Zero)
        {
            // Honors cancellation, so a dashboard load the person cancels stops waiting at once.
            await Task.Delay(faultSwitch.Delay, cancellationToken);
        }

        if (faultSwitch.Fail)
        {
            return Result.Fail<SqliteConnection>("storage is unavailable (test fault switch)");
        }

        try
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            await pragma.ExecuteNonQueryAsync(cancellationToken);
            return Result.Ok(connection);
        }
        catch (SqliteException ex)
        {
            return Result.Fail<SqliteConnection>(new Error("storage could not be opened").CausedBy(ex));
        }
    }
}
