namespace PeakMediaRemoteMcpServer.Usage;

/// <summary>Month-to-date totals of one caller's usage log.</summary>
public sealed record UsageTotals(decimal Spent, int ClipsSynthesized, int CacheHits);

/// <summary>
/// The usage log: one row per clip of a call, owned by the caller's OIDC subject. Every read and write
/// names the acting caller and the owner subject and is decided by <see cref="AccessRule"/> — a caller
/// reaches only their own rows; an administrator may reach anyone's (the dashboard). Narration text is
/// never stored: a row holds the character count and the cache key.
/// </summary>
public sealed class UsageStore(SqliteConnectionFactory connections, TimeProvider clock)
{
    public async Task<Result> RecordAsync(Caller actor, string ownerSubject, UsageEntry entry, CancellationToken cancellationToken)
    {
        if (!AccessRule.CanAccess(actor, ownerSubject))
        {
            return Result.Fail("the caller may not write another caller's usage");
        }

        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return Result.Fail(opened.Errors);
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO usage_log (subject, at_utc, model, voice, characters, cost_nanousd, cache_key, cache_hit)
            VALUES ($subject, $at, $model, $voice, $characters, $cost, $key, $hit);
            """;
        command.Parameters.AddWithValue("$subject", ownerSubject);
        command.Parameters.AddWithValue("$at", clock.GetUtcNow().ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$model", entry.Model);
        command.Parameters.AddWithValue("$voice", entry.Voice);
        command.Parameters.AddWithValue("$characters", entry.Characters);
        command.Parameters.AddWithValue("$cost", entry.CacheHit ? 0L : NanoUsd.From(entry.CostUsd));
        command.Parameters.AddWithValue("$key", entry.CacheKey);
        command.Parameters.AddWithValue("$hit", entry.CacheHit ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<UsageTotals>> GetTotalsAsync(Caller actor, string ownerSubject, UsageMonth month, CancellationToken cancellationToken)
    {
        if (!AccessRule.CanAccess(actor, ownerSubject))
        {
            return Result.Fail<UsageTotals>("the caller may not read another caller's usage");
        }

        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return Result.Fail<UsageTotals>(opened.Errors);
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(cost_nanousd), 0),
                   COALESCE(SUM(CASE WHEN cache_hit = 0 THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(cache_hit), 0)
            FROM usage_log
            WHERE subject = $subject AND at_utc >= $start AND at_utc < $end;
            """;
        command.Parameters.AddWithValue("$subject", ownerSubject);
        command.Parameters.AddWithValue("$start", month.StartUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$end", month.ResetsUtc.ToUnixTimeMilliseconds());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return Result.Ok(new UsageTotals(
            NanoUsd.ToUsd(reader.GetInt64(0)),
            (int)reader.GetInt64(1),
            (int)reader.GetInt64(2)));
    }
}
