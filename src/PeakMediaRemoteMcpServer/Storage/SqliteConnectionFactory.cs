namespace PeakMediaRemoteMcpServer.Storage;

/// <summary>Opens connections to the gateway's SQLite database (WAL mode), honoring the test fault switch.</summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;
    private readonly StorageFaultSwitch _faultSwitch;

    public SqliteConnectionFactory(string databasePath, StorageFaultSwitch faultSwitch)
    {
        DatabasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        _faultSwitch = faultSwitch;
    }

    public string DatabasePath { get; }

    public async Task<Result<SqliteConnection>> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (_faultSwitch.Delay > TimeSpan.Zero)
        {
            await Task.Delay(_faultSwitch.Delay, cancellationToken);
        }

        if (_faultSwitch.Fail)
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
