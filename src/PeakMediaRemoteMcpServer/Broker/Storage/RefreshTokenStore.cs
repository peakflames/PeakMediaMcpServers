namespace PeakMediaRemoteMcpServer.Broker.Storage;

/// <summary>Who a refresh token belongs to.</summary>
public sealed record RefreshTokenRecord(string Subject, string ClientId);

/// <summary>
/// The <c>oauth_refresh_tokens</c> table: the gateway's own opaque refresh tokens, stored hashed and
/// rotated on every use (<see cref="TryConsumeAsync"/> deletes as it reads).
/// </summary>
public sealed class RefreshTokenStore(SqliteConnectionFactory connections)
{
    public async Task<Result> InsertAsync(string refreshToken, RefreshTokenRecord record, DateTimeOffset expiresUtc, CancellationToken cancellationToken = default)
    {
        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return opened.ToResult();
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO oauth_refresh_tokens (token_hash, subject, client_id, expires_utc)
            VALUES ($token_hash, $subject, $client_id, $expires);
            """;
        command.Parameters.AddWithValue("$token_hash", TokenHashing.Hash(refreshToken));
        command.Parameters.AddWithValue("$subject", record.Subject);
        command.Parameters.AddWithValue("$client_id", record.ClientId);
        command.Parameters.AddWithValue("$expires", UnixTime.Of(expiresUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>Deletes the token and returns its owner; null when unknown, already rotated or expired.</summary>
    public async Task<Result<RefreshTokenRecord?>> TryConsumeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return opened.ToResult<RefreshTokenRecord?>();
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM oauth_refresh_tokens
            WHERE token_hash = $token_hash AND expires_utc > $now
            RETURNING subject, client_id;
            """;
        command.Parameters.AddWithValue("$token_hash", TokenHashing.Hash(refreshToken));
        command.Parameters.AddWithValue("$now", UnixTime.Now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result.Ok<RefreshTokenRecord?>(null);
        }

        return Result.Ok<RefreshTokenRecord?>(new RefreshTokenRecord(reader.GetString(0), reader.GetString(1)));
    }

    public async Task<Result<int>> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        return await StoreSql.DeleteExpiredAsync(connections, "oauth_refresh_tokens", cancellationToken);
    }
}
