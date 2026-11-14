namespace PeakMediaRemoteMcpServer.Usage;

/// <summary>One row of the admin Usage table.</summary>
public sealed record AdminUsageRow(string Subject, string Email, decimal Spent, decimal Quota, long Characters, int Clips, int CacheHitRatePercent, bool IsOver);

/// <summary>The Usage table's "Total" footer.</summary>
public sealed record AdminUsageTotal(decimal Spent, long Characters, int Clips, int CacheHitRatePercent, int OverCount);

/// <summary>Every user's month for the admin Usage screen: rows sorted by Spent, highest first, plus the total.</summary>
public sealed record AdminUsageReport(string Month, IReadOnlyList<AdminUsageRow> Rows, AdminUsageTotal Total);

/// <summary>
/// The admin Usage screen's data. The caller always comes from the signed-in session principal
/// (<see cref="Caller.From"/>) and the read goes through <see cref="UsageStore.ListMonthAsync"/>, which
/// refuses anyone without the admin role. The months offered are My Usage's (<see cref="DashboardUsageService.MonthLabels"/>).
/// </summary>
public sealed class AdminUsageService(UsageStore usage, QuotaStore quotas, DashboardUsageService months)
{
    public string CurrentMonthLabel => months.CurrentMonthLabel;

    public IReadOnlyList<string> MonthLabels()
    {
        return months.MonthLabels();
    }

    public async Task<Result<AdminUsageReport>> GetAsync(ClaimsPrincipal principal, string monthLabel, CancellationToken cancellationToken)
    {
        var caller = Caller.From(principal);
        if (caller.IsFailed)
        {
            return Result.Fail<AdminUsageReport>(caller.Errors);
        }

        if (!MonthLabels().Contains(monthLabel, StringComparer.Ordinal)
            || !DateTimeOffset.TryParseExact(monthLabel + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start))
        {
            return Result.Fail<AdminUsageReport>("the month is not one of the months shown");
        }

        var listed = await usage.ListMonthAsync(caller.Value, UsageMonth.Containing(start), quotas.DefaultMonthlyUsd, cancellationToken);
        if (listed.IsFailed)
        {
            return Result.Fail<AdminUsageReport>(listed.Errors);
        }

        var rows = listed.Value
            .OrderByDescending(u => u.Spent)
            .ThenBy(u => u.Email, StringComparer.OrdinalIgnoreCase)
            .Select(u => new AdminUsageRow(u.Subject, u.Email, u.Spent, u.Quota, u.Characters, u.Clips, Percent(u.CacheHits, u.Clips), u.Spent >= u.Quota))
            .ToList();
        var clips = listed.Value.Sum(u => u.Clips);
        var total = new AdminUsageTotal(
            listed.Value.Sum(u => u.Spent),
            listed.Value.Sum(u => u.Characters),
            clips,
            Percent(listed.Value.Sum(u => u.CacheHits), clips),
            rows.Count(r => r.IsOver));
        return Result.Ok(new AdminUsageReport(monthLabel, rows, total));
    }

    /// <summary><paramref name="part"/> of <paramref name="whole"/> as a whole percentage (0 when nothing was requested).</summary>
    public static int Percent(long part, long whole)
    {
        return whole == 0 ? 0 : (int)Math.Round(part * 100m / whole, MidpointRounding.AwayFromZero);
    }
}
