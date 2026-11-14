namespace PeakMediaRemoteMcpServer.Cache;

/// <summary>The shared audio cache's figures for the admin Cache screen.</summary>
/// <param name="SizeBytes">Total size of the cached clips.</param>
/// <param name="CapBytes">The <c>Storage:CacheMaxGb</c> limit in bytes.</param>
/// <param name="Clips">Number of cached clips.</param>
/// <param name="HitRatePercent">Cache hits / all requested clips this month (all users), whole percent.</param>
/// <param name="EvictedThisMonth">Clips the eviction pass removed this month.</param>
public sealed record CacheStats(long SizeBytes, long CapBytes, int Clips, int HitRatePercent, int EvictedThisMonth);

/// <summary>
/// Reads <see cref="CacheStats"/> for an administrator. The caller comes from the signed-in session
/// principal and must hold the admin role (<see cref="AccessRule.RequireAdmin"/>) — the figures cover
/// every user. Storage failures (including the test fault switch) come back as a failed result.
/// </summary>
public sealed class CacheStatsReader(SqliteConnectionFactory connections, CacheEvictor evictor, TimeProvider clock)
{
    public async Task<Result<CacheStats>> GetAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var caller = Caller.From(principal);
        if (caller.IsFailed)
        {
            return Result.Fail<CacheStats>(caller.Errors);
        }

        var allowed = AccessRule.RequireAdmin(caller.Value);
        if (allowed.IsFailed)
        {
            return Result.Fail<CacheStats>(allowed.Errors);
        }

        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return Result.Fail<CacheStats>(opened.Errors);
        }

        try
        {
            await using var connection = opened.Value;
            var month = UsageMonth.Current(clock);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT (SELECT COALESCE(SUM(size_bytes), 0) FROM audio_cache),
                       (SELECT COUNT(*) FROM audio_cache),
                       (SELECT COALESCE(SUM(cache_hit), 0) FROM usage_log WHERE at_utc >= $start AND at_utc < $end),
                       (SELECT COUNT(*) FROM usage_log WHERE at_utc >= $start AND at_utc < $end),
                       (SELECT COUNT(*) FROM cache_evictions WHERE at_utc >= $start AND at_utc < $end);
                """;
            command.Parameters.AddWithValue("$start", month.StartUtc.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$end", month.ResetsUtc.ToUnixTimeMilliseconds());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return Result.Ok(new CacheStats(
                reader.GetInt64(0),
                evictor.MaxBytes,
                (int)reader.GetInt64(1),
                AdminUsageService.Percent(reader.GetInt64(2), reader.GetInt64(3)),
                (int)reader.GetInt64(4)));
        }
        catch (SqliteException ex)
        {
            return Result.Fail<CacheStats>(new Error("cache statistics could not be read").CausedBy(ex));
        }
    }
}
