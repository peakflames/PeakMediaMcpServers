namespace PeakMediaRemoteMcpServer.Usage;

/// <summary>Month-to-date totals of one caller's usage log.</summary>
public sealed record UsageTotals(decimal Spent, int ClipsSynthesized, int CacheHits);

/// <summary>One user's month in the admin usage list. <see cref="Clips"/> counts every requested clip (synthesized and cache hits).</summary>
public sealed record UserMonthUsage(string Subject, string Email, decimal Spent, decimal Quota, long Characters, int Clips, int CacheHits);

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

    /// <summary>
    /// Every user's totals for <paramref name="month"/> (administrators only): one row per user with
    /// usage in the month, with their email (the subject when they have no users row) and their quota
    /// (override, else <paramref name="defaultQuotaUsd"/>). Clips count every requested clip —
    /// synthesized and cache hits.
    /// </summary>
    public async Task<Result<IReadOnlyList<UserMonthUsage>>> ListMonthAsync(Caller admin, UsageMonth month, decimal defaultQuotaUsd, CancellationToken cancellationToken)
    {
        var allowed = AccessRule.RequireAdmin(admin);
        if (allowed.IsFailed)
        {
            return Result.Fail<IReadOnlyList<UserMonthUsage>>(allowed.Errors);
        }

        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return Result.Fail<IReadOnlyList<UserMonthUsage>>(opened.Errors);
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT l.subject,
                   COALESCE(u.email, l.subject),
                   q.monthly_nanousd,
                   SUM(l.cost_nanousd),
                   SUM(l.characters),
                   COUNT(*),
                   SUM(l.cache_hit)
            FROM usage_log l
            LEFT JOIN users u ON u.subject = l.subject
            LEFT JOIN quota_overrides q ON q.subject = l.subject
            WHERE l.at_utc >= $start AND l.at_utc < $end
            GROUP BY l.subject;
            """;
        command.Parameters.AddWithValue("$start", month.StartUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$end", month.ResetsUtc.ToUnixTimeMilliseconds());
        var rows = new List<UserMonthUsage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new UserMonthUsage(
                reader.GetString(0),
                reader.GetString(1),
                NanoUsd.ToUsd(reader.GetInt64(3)),
                reader.IsDBNull(2) ? defaultQuotaUsd : NanoUsd.ToUsd(reader.GetInt64(2)),
                reader.GetInt64(4),
                (int)reader.GetInt64(5),
                (int)reader.GetInt64(6)));
        }

        return Result.Ok<IReadOnlyList<UserMonthUsage>>(rows);
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
