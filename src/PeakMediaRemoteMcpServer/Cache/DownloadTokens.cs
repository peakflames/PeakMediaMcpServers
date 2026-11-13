namespace PeakMediaRemoteMcpServer.Cache;

/// <summary>What a download token resolves to.</summary>
public enum DownloadLookup
{
    Found,
    Unknown,
    Expired,
}

/// <summary>
/// Short-lived download links. A token is 32 random bytes in base64url (43 characters) — unrelated to
/// the clip's cache key — issued fresh on every result, reusable for 15 minutes, and owned by the
/// caller it was issued to. Only its SHA-256 hash is stored.
/// </summary>
public sealed class DownloadTokens(SqliteConnectionFactory connections, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public async Task<Result<string>> IssueAsync(string cacheKey, Caller owner, CancellationToken cancellationToken)
    {
        var token = System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var now = clock.GetUtcNow();
        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return Result.Fail<string>(opened.Errors);
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO clip_downloads (token_hash, cache_key, subject, created_utc, expires_utc)
            VALUES ($hash, $key, $subject, $created, $expires);
            """;
        command.Parameters.AddWithValue("$hash", TokenHashing.Hash(token));
        command.Parameters.AddWithValue("$key", cacheKey);
        command.Parameters.AddWithValue("$subject", owner.Subject);
        command.Parameters.AddWithValue("$created", now.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$expires", (now + Lifetime).ToUnixTimeMilliseconds());
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Result.Ok(token);
    }

    /// <summary>Resolves a presented token to its clip's cache key.</summary>
    public async Task<Result<(DownloadLookup Lookup, string? CacheKey)>> ResolveAsync(string token, CancellationToken cancellationToken)
    {
        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return Result.Fail<(DownloadLookup, string?)>(opened.Errors);
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT cache_key, expires_utc FROM clip_downloads WHERE token_hash = $hash;";
        command.Parameters.AddWithValue("$hash", TokenHashing.Hash(token));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result.Ok<(DownloadLookup, string?)>((DownloadLookup.Unknown, null));
        }

        var cacheKey = reader.GetString(0);
        var expires = reader.GetInt64(1);
        return clock.GetUtcNow().ToUnixTimeMilliseconds() >= expires
            ? Result.Ok<(DownloadLookup, string?)>((DownloadLookup.Expired, cacheKey))
            : Result.Ok<(DownloadLookup, string?)>((DownloadLookup.Found, cacheKey));
    }
}
