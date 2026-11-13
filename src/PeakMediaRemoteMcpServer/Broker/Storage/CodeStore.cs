namespace PeakMediaRemoteMcpServer.Broker.Storage;

/// <summary>What a one-time authorization code was issued for.</summary>
public sealed record AuthorizationCodeRecord(string Subject, string ClientId, string ClientRedirectUri, string ClientCodeChallenge);

/// <summary>
/// The <c>oauth_codes</c> table: one-time codes handed to the client at the end of
/// <c>/oauth/callback</c>. Only the hash is stored; <see cref="TryConsumeAsync"/> deletes and returns
/// in one statement so a code can never be redeemed twice.
/// </summary>
public sealed class CodeStore(SqliteConnectionFactory connections)
{
    public async Task<Result> InsertAsync(string code, AuthorizationCodeRecord record, DateTimeOffset expiresUtc, CancellationToken cancellationToken = default)
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
            INSERT INTO oauth_codes (code_hash, subject, client_id, client_redirect_uri, client_code_challenge, expires_utc)
            VALUES ($code_hash, $subject, $client_id, $redirect_uri, $challenge, $expires);
            """;
        command.Parameters.AddWithValue("$code_hash", TokenHashing.Hash(code));
        command.Parameters.AddWithValue("$subject", record.Subject);
        command.Parameters.AddWithValue("$client_id", record.ClientId);
        command.Parameters.AddWithValue("$redirect_uri", record.ClientRedirectUri);
        command.Parameters.AddWithValue("$challenge", record.ClientCodeChallenge);
        command.Parameters.AddWithValue("$expires", UnixTime.Of(expiresUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>Deletes the code and returns what it was issued for; null when unknown, used or expired.</summary>
    public async Task<Result<AuthorizationCodeRecord?>> TryConsumeAsync(string code, CancellationToken cancellationToken = default)
    {
        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return opened.ToResult<AuthorizationCodeRecord?>();
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM oauth_codes
            WHERE code_hash = $code_hash AND expires_utc > $now
            RETURNING subject, client_id, client_redirect_uri, client_code_challenge;
            """;
        command.Parameters.AddWithValue("$code_hash", TokenHashing.Hash(code));
        command.Parameters.AddWithValue("$now", UnixTime.Now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result.Ok<AuthorizationCodeRecord?>(null);
        }

        return Result.Ok<AuthorizationCodeRecord?>(new AuthorizationCodeRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
    }

    public async Task<Result<int>> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        return await StoreSql.DeleteExpiredAsync(connections, "oauth_codes", cancellationToken);
    }
}
