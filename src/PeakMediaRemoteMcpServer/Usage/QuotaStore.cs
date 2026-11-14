namespace PeakMediaRemoteMcpServer.Usage;

/// <summary>A caller's monthly quota in US dollars and whether it is the configured default (no override).</summary>
public sealed record QuotaInfo(decimal Usd, bool IsDefault);

/// <summary>
/// Monthly quotas: the configured default (<c>Quota:DefaultMonthlyUsd</c>) unless an administrator
/// has set a per-user override, which records who set it and when. Reads are decided by
/// <see cref="AccessRule.CanAccess"/>; setting an override needs the admin role
/// (<see cref="AccessRule.RequireAdmin"/>). The dashboard calls <see cref="SetOverrideAsync"/> and
/// <see cref="ClearOverrideAsync"/>.
/// </summary>
public sealed class QuotaStore(SqliteConnectionFactory connections, IOptions<QuotaOptions> options, TimeProvider clock)
{
    public decimal DefaultMonthlyUsd => options.Value.DefaultMonthlyUsd;

    public async Task<Result<decimal>> GetQuotaAsync(Caller actor, string subject, CancellationToken cancellationToken)
    {
        var info = await GetQuotaInfoAsync(actor, subject, cancellationToken);
        return info.IsFailed ? Result.Fail<decimal>(info.Errors) : Result.Ok(info.Value.Usd);
    }

    /// <summary>The caller's quota and whether it is the default (no administrator override exists).</summary>
    public async Task<Result<QuotaInfo>> GetQuotaInfoAsync(Caller actor, string subject, CancellationToken cancellationToken)
    {
        if (!AccessRule.CanAccess(actor, subject))
        {
            return Result.Fail<QuotaInfo>("the caller may not read another caller's quota");
        }

        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return Result.Fail<QuotaInfo>(opened.Errors);
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT monthly_nanousd FROM quota_overrides WHERE subject = $subject;";
        command.Parameters.AddWithValue("$subject", subject);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Result.Ok(value is long nano ? new QuotaInfo(NanoUsd.ToUsd(nano), false) : new QuotaInfo(DefaultMonthlyUsd, true));
    }

    public async Task<Result> SetOverrideAsync(Caller admin, string subject, decimal monthlyUsd, CancellationToken cancellationToken = default)
    {
        var allowed = AccessRule.RequireAdmin(admin);
        if (allowed.IsFailed)
        {
            return allowed;
        }

        if (monthlyUsd < 0m)
        {
            return Result.Fail("a monthly quota cannot be negative");
        }

        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return Result.Fail(opened.Errors);
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO quota_overrides (subject, monthly_nanousd, set_by_subject, set_at_utc)
            VALUES ($subject, $quota, $by, $at)
            ON CONFLICT(subject) DO UPDATE SET monthly_nanousd = $quota, set_by_subject = $by, set_at_utc = $at;
            """;
        command.Parameters.AddWithValue("$subject", subject);
        command.Parameters.AddWithValue("$quota", NanoUsd.From(monthlyUsd));
        command.Parameters.AddWithValue("$by", admin.Subject);
        command.Parameters.AddWithValue("$at", clock.GetUtcNow().ToUnixTimeMilliseconds());
        await command.ExecuteNonQueryAsync(cancellationToken);
        Log.Information("Monthly quota override set for a caller by an administrator");
        return Result.Ok();
    }

    /// <summary>Removes <paramref name="subject"/>'s override so the default quota applies from their next call (administrators only).</summary>
    public async Task<Result> ClearOverrideAsync(Caller admin, string subject, CancellationToken cancellationToken = default)
    {
        var allowed = AccessRule.RequireAdmin(admin);
        if (allowed.IsFailed)
        {
            return allowed;
        }

        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return Result.Fail(opened.Errors);
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM quota_overrides WHERE subject = $subject;";
        command.Parameters.AddWithValue("$subject", subject);
        await command.ExecuteNonQueryAsync(cancellationToken);
        Log.Information("Monthly quota override cleared for a caller by an administrator");
        return Result.Ok();
    }
}
