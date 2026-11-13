namespace PeakMediaRemoteMcpServer.Usage;

/// <summary>
/// My Usage's data: the months a person can pick (the current one and the eleven before it) and their
/// own standing for a picked month. The caller always comes from the signed-in session principal
/// (<see cref="Caller.From"/>), never from anything on the page.
/// </summary>
public sealed class DashboardUsageService(UsageStatusReader reader, TimeProvider clock)
{
    public const int MonthsShown = 12;

    public string CurrentMonthLabel => UsageMonth.Current(clock).Label;

    /// <summary>Month labels, newest first: the current month and the previous eleven.</summary>
    public IReadOnlyList<string> MonthLabels()
    {
        var current = UsageMonth.Current(clock).StartUtc;
        return [.. Enumerable.Range(0, MonthsShown).Select(i => current.AddMonths(-i).ToString("yyyy-MM", CultureInfo.InvariantCulture))];
    }

    public async Task<Result<UsageStatus>> GetAsync(ClaimsPrincipal principal, string monthLabel, CancellationToken cancellationToken)
    {
        var caller = Caller.From(principal);
        if (caller.IsFailed)
        {
            return Result.Fail<UsageStatus>(caller.Errors);
        }

        if (!MonthLabels().Contains(monthLabel, StringComparer.Ordinal)
            || !DateTimeOffset.TryParseExact(monthLabel + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start))
        {
            return Result.Fail<UsageStatus>("the month is not one of the months shown");
        }

        return await reader.ReadAsync(caller.Value, UsageMonth.Containing(start), cancellationToken);
    }
}
