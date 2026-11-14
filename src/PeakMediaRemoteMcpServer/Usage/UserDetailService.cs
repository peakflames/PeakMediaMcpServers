namespace PeakMediaRemoteMcpServer.Usage;

/// <summary>One user's month for User Detail (S-03): the summary, their quota, and one row per clip, newest first.</summary>
public sealed record UserDetailReport(
    string Subject,
    string Email,
    string Month,
    decimal Spent,
    decimal Quota,
    bool QuotaIsDefault,
    decimal DefaultQuota,
    int Clips,
    int CacheHits,
    int CacheHitPercent,
    bool IsOver,
    IReadOnlyList<UsageClipRow> Rows);

/// <summary>How a quota change on User Detail ended.</summary>
public enum QuotaChangeOutcome
{
    /// <summary>The change was stored; it applies to the person's next call.</summary>
    Saved,

    /// <summary>The amount was not a valid quota; nothing was stored.</summary>
    Invalid,

    /// <summary>The data source could not store the change; nothing was stored.</summary>
    SaveFailed,
}

/// <summary>The result of a quota change, with the exact text User Detail shows.</summary>
public sealed record QuotaChange(QuotaChangeOutcome Outcome, string Message);

/// <summary>
/// User Detail's data and quota actions. The caller always comes from the signed-in session principal
/// (<see cref="Caller.From"/>); every read and write goes through <see cref="UsageStore"/> and
/// <see cref="QuotaStore"/>, which refuse anyone without the admin role (<see cref="AccessRule"/>).
/// </summary>
public sealed class UserDetailService(UsageStore usage, QuotaStore quotas, DashboardUsageService months)
{
    public const string InvalidAmountMessage = "Enter an amount of $0.00 or more, for example 60.";
    public const string TooLargeAmountMessage = "Enter an amount of $1,000,000.00 or less, for example 60.";
    public const string SaveFailedMessage = "Couldn't save the quota. Check your connection and press Save again.";
    public const string ResetFailedMessage = "Couldn't reset the quota. Check your connection and press Use default again.";
    public const string NotAllowedMessage = "Only gateway administrators can change quotas. Sign in as an administrator and try again.";

    /// <summary>The largest quota the form accepts.</summary>
    public const decimal MaxMonthlyUsd = 1_000_000m;

    public string CurrentMonthLabel => months.CurrentMonthLabel;

    public decimal DefaultMonthlyUsd => quotas.DefaultMonthlyUsd;

    public IReadOnlyList<string> MonthLabels()
    {
        return months.MonthLabels();
    }

    /// <summary>
    /// <paramref name="user"/>'s month — <paramref name="user"/> is how the Usage table names them (email, or subject
    /// when they have no users row). Unknown users and other months than those shown fail.
    /// </summary>
    public async Task<Result<UserDetailReport>> GetAsync(ClaimsPrincipal principal, string user, string monthLabel, CancellationToken cancellationToken)
    {
        var caller = Caller.From(principal);
        if (caller.IsFailed)
        {
            return Result.Fail<UserDetailReport>(caller.Errors);
        }

        if (!MonthLabels().Contains(monthLabel, StringComparer.Ordinal)
            || !DateTimeOffset.TryParseExact(monthLabel + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start))
        {
            return Result.Fail<UserDetailReport>("the month is not one of the months shown");
        }

        var listed = await usage.ListUserMonthAsync(caller.Value, user, UsageMonth.Containing(start), cancellationToken);
        if (listed.IsFailed)
        {
            return Result.Fail<UserDetailReport>(listed.Errors);
        }

        if (!listed.Value.Known)
        {
            return Result.Fail<UserDetailReport>("the gateway has no such user");
        }

        var subject = listed.Value.Subject;

        var quota = await quotas.GetQuotaInfoAsync(caller.Value, subject, cancellationToken);
        if (quota.IsFailed)
        {
            return Result.Fail<UserDetailReport>(quota.Errors);
        }

        var rows = listed.Value.Rows;
        var spent = rows.Sum(r => r.Cost);
        var hits = rows.Count(r => r.CacheHit);
        return Result.Ok(new UserDetailReport(
            subject,
            listed.Value.Email,
            monthLabel,
            spent,
            quota.Value.Usd,
            quota.Value.IsDefault,
            quotas.DefaultMonthlyUsd,
            rows.Count,
            hits,
            AdminUsageService.Percent(hits, rows.Count),
            spent >= quota.Value.Usd,
            rows));
    }

    /// <summary>
    /// Sets <paramref name="subject"/>'s monthly quota to the amount typed in "Monthly quota (USD)".
    /// An invalid amount stores nothing and returns <see cref="QuotaChangeOutcome.Invalid"/>.
    /// </summary>
    public async Task<QuotaChange> SaveQuotaAsync(ClaimsPrincipal principal, string subject, string email, string? amountText, CancellationToken cancellationToken)
    {
        var parsed = ParseAmount(amountText);
        if (parsed.IsFailed)
        {
            return new QuotaChange(QuotaChangeOutcome.Invalid, parsed.Errors[0].Message);
        }

        var caller = Caller.From(principal);
        if (caller.IsFailed || !caller.Value.IsAdmin)
        {
            return new QuotaChange(QuotaChangeOutcome.SaveFailed, NotAllowedMessage);
        }

        var before = await quotas.GetQuotaInfoAsync(caller.Value, subject, cancellationToken);
        if (before.IsFailed)
        {
            return new QuotaChange(QuotaChangeOutcome.SaveFailed, SaveFailedMessage);
        }

        var set = await quotas.SetOverrideAsync(caller.Value, subject, parsed.Value, cancellationToken);
        if (set.IsFailed)
        {
            return new QuotaChange(QuotaChangeOutcome.SaveFailed, SaveFailedMessage);
        }

        var was = before.Value.IsDefault
            ? $"{UsageText.Dollars(before.Value.Usd)}, the default"
            : UsageText.Dollars(before.Value.Usd);
        return new QuotaChange(
            QuotaChangeOutcome.Saved,
            $"Quota set to {UsageText.Dollars(parsed.Value)}/month (was {was}). Applies to {DisplayName(email)}'s next call.");
    }

    /// <summary>Removes <paramref name="subject"/>'s override; the default applies from their next call.</summary>
    public async Task<QuotaChange> ResetQuotaAsync(ClaimsPrincipal principal, string subject, CancellationToken cancellationToken)
    {
        var caller = Caller.From(principal);
        if (caller.IsFailed || !caller.Value.IsAdmin)
        {
            return new QuotaChange(QuotaChangeOutcome.SaveFailed, NotAllowedMessage);
        }

        var cleared = await quotas.ClearOverrideAsync(caller.Value, subject, cancellationToken);
        return cleared.IsFailed
            ? new QuotaChange(QuotaChangeOutcome.SaveFailed, ResetFailedMessage)
            : new QuotaChange(QuotaChangeOutcome.Saved, $"Quota reset to the default ({UsageText.Dollars(quotas.DefaultMonthlyUsd)}/month).");
    }

    /// <summary>A quota amount in US dollars ("60", "60.5", "$1,200.00"), rounded to the cent; negative or unreadable amounts fail.</summary>
    public static Result<decimal> ParseAmount(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.StartsWith('$'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0m)
        {
            return Result.Fail<decimal>(InvalidAmountMessage);
        }

        if (amount > MaxMonthlyUsd)
        {
            return Result.Fail<decimal>(TooLargeAmountMessage);
        }

        return Result.Ok(Math.Round(amount, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>A short name for messages: the first word of the email's local part, capitalised ("pat.lee@…" → "Pat").</summary>
    public static string DisplayName(string email)
    {
        var local = email.Split('@')[0];
        var first = local.Split(['.', '_', '-', '+'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? local;
        return first.Length == 0 ? email : char.ToUpperInvariant(first[0]) + first[1..];
    }
}
