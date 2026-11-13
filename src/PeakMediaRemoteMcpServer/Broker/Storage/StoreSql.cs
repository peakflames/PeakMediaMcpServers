namespace PeakMediaRemoteMcpServer.Broker.Storage;

/// <summary>Small shared statements for the broker stores (parameterized; table names are constants).</summary>
internal static class StoreSql
{
    public static async Task<Result> ExecuteAsync(SqliteConnectionFactory connections, string sql, string parameter, CancellationToken cancellationToken)
    {
        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return opened.ToResult();
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$p", parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>Deletes rows whose <c>expires_utc</c> has passed. <paramref name="table"/> is always a compile-time constant.</summary>
    public static async Task<Result<int>> DeleteExpiredAsync(SqliteConnectionFactory connections, string table, CancellationToken cancellationToken)
    {
        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return opened.ToResult<int>();
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE expires_utc <= $now;";
        command.Parameters.AddWithValue("$now", UnixTime.Now);
        return Result.Ok(await command.ExecuteNonQueryAsync(cancellationToken));
    }
}
