namespace PeakMediaRemoteMcpServer.Usage;

/// <summary>Month-to-date totals of one caller's usage log.</summary>
public sealed record UsageTotals(decimal Spent, int ClipsSynthesized, int CacheHits);

/// <summary>One user's month in the admin usage list. <see cref="Clips"/> counts every requested clip (synthesized and cache hits).</summary>
public sealed record UserMonthUsage(string Subject, string Email, decimal Spent, decimal Quota, long Characters, int Clips, int CacheHits);

/// <summary>One clip of a user's month on User Detail: never the narration text (the log does not hold it).</summary>
public sealed record UsageClipRow(DateTimeOffset AtUtc, string Model, string Voice, long Characters, decimal Cost, bool CacheHit);

/// <summary>
/// One user's clips in a month, newest first, with their subject and email (the subject when they have no
/// users row). <see cref="Known"/> is false when no user matches (no users row and no usage ever).
/// </summary>
public sealed record UserMonthClips(string Subject, string Email, bool Known, IReadOnlyList<UsageClipRow> Rows);

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

    /// <summary>
    /// One user's clips in <paramref name="month"/>, newest first (administrators only). <paramref name="user"/> is
    /// how the Usage table names the person: their subject, or the email of exactly one users row.
    /// </summary>
    public async Task<Result<UserMonthClips>> ListUserMonthAsync(Caller admin, string user, UsageMonth month, CancellationToken cancellationToken)
    {
        var allowed = AccessRule.RequireAdmin(admin);
        if (allowed.IsFailed)
        {
            return Result.Fail<UserMonthClips>(allowed.Errors);
        }

        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return Result.Fail<UserMonthClips>(opened.Errors);
        }

        await using var connection = opened.Value;
        string? subject = null;
        string? email = null;
        await using (var who = connection.CreateCommand())
        {
            who.CommandText = """
                SELECT subject, email FROM users WHERE subject = $user
                UNION ALL
                SELECT subject, email FROM users WHERE email = $user COLLATE NOCASE AND subject <> $user
                    AND NOT EXISTS (SELECT 1 FROM users WHERE subject = $user);
                """;
            who.Parameters.AddWithValue("$user", user);
            await using var found = await who.ExecuteReaderAsync(cancellationToken);
            var matches = 0;
            while (await found.ReadAsync(cancellationToken))
            {
                matches++;
                subject = found.GetString(0);
                email = found.GetString(1);
            }

            if (matches > 1)
            {
                return Result.Fail<UserMonthClips>("more than one user has this email");
            }
        }

        if (subject is null)
        {
            await using var logged = connection.CreateCommand();
            logged.CommandText = "SELECT EXISTS (SELECT 1 FROM usage_log WHERE subject = $user);";
            logged.Parameters.AddWithValue("$user", user);
            if (Convert.ToInt64(await logged.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 0)
            {
                return Result.Ok(new UserMonthClips(user, user, false, []));
            }

            subject = user;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT at_utc, model, voice, characters, cost_nanousd, cache_hit
            FROM usage_log
            WHERE subject = $subject AND at_utc >= $start AND at_utc < $end
            ORDER BY at_utc DESC, rowid DESC;
            """;
        command.Parameters.AddWithValue("$subject", subject);
        command.Parameters.AddWithValue("$start", month.StartUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$end", month.ResetsUtc.ToUnixTimeMilliseconds());
        var rows = new List<UsageClipRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new UsageClipRow(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                NanoUsd.ToUsd(reader.GetInt64(4)),
                reader.GetInt64(5) != 0));
        }

        return Result.Ok(new UserMonthClips(subject, email ?? subject, true, rows));
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
