namespace PeakMediaRemoteMcpServer.Broker.Storage;

/// <summary>Key/value access to <c>app_meta</c> (the token-signing key lives here).</summary>
public sealed class AppMetaStore(SqliteConnectionFactory connections)
{
    public async Task<Result<string?>> TryGetAsync(string key, CancellationToken cancellationToken = default)
    {
        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return opened.ToResult<string?>();
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_meta WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return Result.Ok(await command.ExecuteScalarAsync(cancellationToken) as string);
    }

    /// <summary>Inserts the value unless the key already exists; returns the value now stored.</summary>
    public async Task<Result<string>> GetOrAddAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return opened.ToResult<string>();
        }

        await using var connection = opened.Value;
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO app_meta (key, value) VALUES ($key, $value) ON CONFLICT(key) DO NOTHING;";
            insert.Parameters.AddWithValue("$key", key);
            insert.Parameters.AddWithValue("$value", value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT value FROM app_meta WHERE key = $key;";
        read.Parameters.AddWithValue("$key", key);
        return Result.Ok((string)(await read.ExecuteScalarAsync(cancellationToken))!);
    }
}
