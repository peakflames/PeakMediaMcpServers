namespace PeakMediaRemoteMcpServer.Usage;

/// <summary>
/// A calendar month in UTC: month-to-date spend counts from 00:00 UTC on the first day, and the quota
/// resets at 00:00 UTC on the first day of the next month.
/// </summary>
public sealed record UsageMonth(DateTimeOffset StartUtc, DateTimeOffset ResetsUtc)
{
    /// <summary>The month label, e.g. <c>2026-11</c>.</summary>
    public string Label => StartUtc.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static UsageMonth Containing(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        var start = new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return new UsageMonth(start, start.AddMonths(1));
    }

    public static UsageMonth Current(TimeProvider clock)
    {
        return Containing(clock.GetUtcNow());
    }
}

/// <summary>Converts US dollars to and from the integer nano-dollars the database stores (exact sums).</summary>
public static class NanoUsd
{
    private const decimal PerDollar = 1_000_000_000m;

    public static long From(decimal usd)
    {
        return (long)Math.Round(usd * PerDollar, MidpointRounding.AwayFromZero);
    }

    public static decimal ToUsd(long nanoUsd)
    {
        return nanoUsd / PerDollar;
    }
}
