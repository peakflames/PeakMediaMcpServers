namespace PeakMediaRemoteMcpServer.Broker.Storage;

/// <summary>
/// One in-flight <c>/authorize</c> → OIDC sign-in → <c>/oauth/callback</c> round trip. Holds the
/// gateway's own upstream PKCE verifier, so it is a <c>sealed class</c> (no printing <c>ToString</c>).
/// </summary>
public sealed class OAuthTransaction
{
    public required string TxnId { get; init; }

    public required string ClientId { get; init; }

    public required string ClientRedirectUri { get; init; }

    public required string ClientState { get; init; }

    public required string ClientCodeChallenge { get; init; }

    public required string UpstreamCodeVerifier { get; init; }

    public required string Nonce { get; init; }

    public required string ConsentTokenHash { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required DateTimeOffset ExpiresUtc { get; init; }
}

/// <summary>The <c>oauth_transactions</c> table.</summary>
public sealed class TransactionStore(SqliteConnectionFactory connections)
{
    public async Task<Result> InsertAsync(OAuthTransaction transaction, CancellationToken cancellationToken = default)
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
            INSERT INTO oauth_transactions
                (txn_id, client_id, client_redirect_uri, client_state, client_code_challenge,
                 upstream_code_verifier, nonce, consent_token_hash, created_utc, expires_utc)
            VALUES
                ($txn_id, $client_id, $redirect_uri, $state, $challenge, $verifier, $nonce, $consent, $created, $expires);
            """;
        command.Parameters.AddWithValue("$txn_id", transaction.TxnId);
        command.Parameters.AddWithValue("$client_id", transaction.ClientId);
        command.Parameters.AddWithValue("$redirect_uri", transaction.ClientRedirectUri);
        command.Parameters.AddWithValue("$state", transaction.ClientState);
        command.Parameters.AddWithValue("$challenge", transaction.ClientCodeChallenge);
        command.Parameters.AddWithValue("$verifier", transaction.UpstreamCodeVerifier);
        command.Parameters.AddWithValue("$nonce", transaction.Nonce);
        command.Parameters.AddWithValue("$consent", transaction.ConsentTokenHash);
        command.Parameters.AddWithValue("$created", UnixTime.Of(transaction.CreatedUtc));
        command.Parameters.AddWithValue("$expires", UnixTime.Of(transaction.ExpiresUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>Returns null for an unknown or expired transaction alike.</summary>
    public async Task<Result<OAuthTransaction?>> TryGetAsync(string txnId, CancellationToken cancellationToken = default)
    {
        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return opened.ToResult<OAuthTransaction?>();
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT txn_id, client_id, client_redirect_uri, client_state, client_code_challenge,
                   upstream_code_verifier, nonce, consent_token_hash, created_utc, expires_utc
            FROM oauth_transactions
            WHERE txn_id = $txn_id AND expires_utc > $now;
            """;
        command.Parameters.AddWithValue("$txn_id", txnId);
        command.Parameters.AddWithValue("$now", UnixTime.Now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result.Ok<OAuthTransaction?>(null);
        }

        return Result.Ok<OAuthTransaction?>(new OAuthTransaction
        {
            TxnId = reader.GetString(0),
            ClientId = reader.GetString(1),
            ClientRedirectUri = reader.GetString(2),
            ClientState = reader.GetString(3),
            ClientCodeChallenge = reader.GetString(4),
            UpstreamCodeVerifier = reader.GetString(5),
            Nonce = reader.GetString(6),
            ConsentTokenHash = reader.GetString(7),
            CreatedUtc = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(8)),
            ExpiresUtc = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(9)),
        });
    }

    public async Task<Result> DeleteAsync(string txnId, CancellationToken cancellationToken = default)
    {
        return await StoreSql.ExecuteAsync(connections, "DELETE FROM oauth_transactions WHERE txn_id = $p;", txnId, cancellationToken);
    }

    public async Task<Result<int>> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        return await StoreSql.DeleteExpiredAsync(connections, "oauth_transactions", cancellationToken);
    }
}
