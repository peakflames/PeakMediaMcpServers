namespace PeakMediaMcpTools.Narration;

/// <summary>
/// The signed-in caller's standing for the current calendar month (UTC): what they have spent, their
/// monthly quota, what remains, how many clips were synthesized and served from the cache, and when the
/// month resets.
/// </summary>
public sealed record UsageStatus(
    string Month,
    decimal Spent,
    decimal Quota,
    decimal Remaining,
    int ClipsSynthesized,
    int CacheHits,
    DateTimeOffset ResetsUtc)
{
    /// <summary>True once month-to-date spend has reached the quota: the next call is refused.</summary>
    public bool IsOverQuota => Spent >= Quota;
}

/// <summary>
/// One clip of a call as charged to the caller. Carries the character count and cache key only —
/// never the narration text.
/// </summary>
public sealed record UsageEntry(string Model, string Voice, int Characters, decimal CostUsd, string CacheKey, bool CacheHit);

/// <summary>
/// The usage log and monthly quota of the signed-in caller. The gateway implements it over SQLite,
/// resolving the caller from the validated access token, so a caller only ever reads and writes their own records.
/// </summary>
public interface IUsageLedger
{
    /// <summary>The caller's month-to-date standing.</summary>
    Task<Result<UsageStatus>> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>Records one clip of a call against the caller.</summary>
    Task<Result> RecordAsync(UsageEntry entry, CancellationToken cancellationToken);
}

/// <summary>Formats money and times the way usage is shown to callers: US dollars to the cent, UTC.</summary>
public static class UsageText
{
    public static string Dollars(decimal amount)
    {
        var cents = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture, $"${cents:0.00}");
    }

    public static string Utc(DateTimeOffset time)
    {
        return time.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }
}
