namespace PeakMediaRemoteMcpServer.Usage;

/// <summary>
/// Monthly quotas: the configured default (<c>Quota:DefaultMonthlyUsd</c>) unless an administrator
/// has set a per-user override, which records who set it and when. Reads are decided by
/// <see cref="AccessRule.CanAccess"/>; setting an override needs the admin role
/// (<see cref="AccessRule.RequireAdmin"/>). The dashboard calls <see cref="SetOverrideAsync"/>.
/// </summary>
public sealed class QuotaStore(SqliteConnectionFactory connections, IOptions<QuotaOptions> options, TimeProvider clock)
{
    public decimal DefaultMonthlyUsd => options.Value.DefaultMonthlyUsd;

    public async Task<Result<decimal>> GetQuotaAsync(Caller actor, string subject, CancellationToken cancellationToken)
    {
        if (!AccessRule.CanAccess(actor, subject))
        {
            return Result.Fail<decimal>("the caller may not read another caller's quota");
        }

        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return Result.Fail<decimal>(opened.Errors);
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT monthly_nanousd FROM quota_overrides WHERE subject = $subject;";
        command.Parameters.AddWithValue("$subject", subject);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Result.Ok(value is long nano ? NanoUsd.ToUsd(nano) : DefaultMonthlyUsd);
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
}
