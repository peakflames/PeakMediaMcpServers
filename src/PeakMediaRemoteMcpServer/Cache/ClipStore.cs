namespace PeakMediaRemoteMcpServer.Cache;

/// <summary>
/// The gateway's shared audio cache: an <c>audio_cache</c> row per clip and its WAV file in the audio
/// directory. The cache is shared by design — any signed-in caller's identical request is a hit — so
/// it is read by key; each row records the subject that created it. Download links are issued to the
/// signed-in caller resolved from the validated access token (<see cref="Caller.From"/>) and nothing else.
/// </summary>
public sealed class ClipStore(
    SqliteConnectionFactory connections,
    AudioDirectory audio,
    DownloadTokens tokens,
    IHttpContextAccessor httpContext,
    IOptions<McpAuthOptions> mcpAuth,
    TimeProvider clock) : IClipStore
{
    public async Task<Result<CachedClip?>> FindAsync(string cacheKey, CancellationToken cancellationToken)
    {
        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return Result.Fail<CachedClip?>(opened.Errors);
        }

        await using var connection = opened.Value;
        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT duration_ms, size_bytes FROM audio_cache WHERE cache_key = $key;";
        select.Parameters.AddWithValue("$key", cacheKey);
        int durationMs;
        long sizeBytes;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return Result.Ok<CachedClip?>(null);
            }

            durationMs = reader.GetInt32(0);
            sizeBytes = reader.GetInt64(1);
        }

        if (!File.Exists(audio.FileFor(cacheKey)))
        {
            // The index outlived its file: treat as a miss so the clip is synthesized and stored again.
            await using var forget = connection.CreateCommand();
            forget.CommandText = "DELETE FROM audio_cache WHERE cache_key = $key;";
            forget.Parameters.AddWithValue("$key", cacheKey);
            await forget.ExecuteNonQueryAsync(cancellationToken);
            return Result.Ok<CachedClip?>(null);
        }

        await using var touch = connection.CreateCommand();
        touch.CommandText = "UPDATE audio_cache SET last_used_utc = $now WHERE cache_key = $key;";
        touch.Parameters.AddWithValue("$now", clock.GetUtcNow().ToUnixTimeMilliseconds());
        touch.Parameters.AddWithValue("$key", cacheKey);
        await touch.ExecuteNonQueryAsync(cancellationToken);
        return Result.Ok<CachedClip?>(new CachedClip(cacheKey, durationMs, sizeBytes));
    }

    public async Task<Result> SaveAsync(string cacheKey, byte[] wav, int durationMs, CancellationToken cancellationToken)
    {
        var caller = CurrentCaller();
        if (caller.IsFailed)
        {
            return Result.Fail(caller.Errors);
        }

        var path = audio.FileFor(cacheKey);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temporary, wav, cancellationToken);
        File.Move(temporary, path, overwrite: true);

        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return Result.Fail(opened.Errors);
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO audio_cache (cache_key, size_bytes, duration_ms, created_by_subject, created_utc, last_used_utc)
            VALUES ($key, $size, $duration, $subject, $now, $now)
            ON CONFLICT(cache_key) DO UPDATE SET size_bytes = $size, duration_ms = $duration, last_used_utc = $now;
            """;
        command.Parameters.AddWithValue("$key", cacheKey);
        command.Parameters.AddWithValue("$size", wav.LongLength);
        command.Parameters.AddWithValue("$duration", durationMs);
        command.Parameters.AddWithValue("$subject", caller.Value.Subject);
        command.Parameters.AddWithValue("$now", clock.GetUtcNow().ToUnixTimeMilliseconds());
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Result.Ok();
    }

    public string LocationKey => "url";

    public async Task<Result<string>> LocateAsync(string cacheKey, CancellationToken cancellationToken)
    {
        var caller = CurrentCaller();
        if (caller.IsFailed)
        {
            return Result.Fail<string>(caller.Errors);
        }

        var token = await tokens.IssueAsync(cacheKey, caller.Value, cancellationToken);
        if (token.IsFailed)
        {
            return Result.Fail<string>(token.Errors);
        }

        return Result.Ok($"{PublicOrigin()}{ClipEndpoints.PathPrefix}{token.Value}");
    }

    private Result<Caller> CurrentCaller()
    {
        var user = httpContext.HttpContext?.User;
        return user is null ? Result.Fail<Caller>("the caller is not signed in") : Caller.From(user);
    }

    private string PublicOrigin()
    {
        return new Uri(mcpAuth.Value.Resource).GetLeftPart(UriPartial.Authority);
    }
}
