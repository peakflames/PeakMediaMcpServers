namespace PeakMediaMcpServer;

/// <summary>
/// The stdio server's usage log: none. A solo user pays OpenRouter directly with their own key, so there
/// is no quota to enforce and nothing to record — and no database. The caller is never over quota.
/// </summary>
internal sealed class UnmeteredUsageLedger(TimeProvider clock) : IUsageLedger
{
    public Task<Result<UsageStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var status = new UsageStatus(
            monthStart.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
            Spent: 0m,
            Quota: decimal.MaxValue,
            Remaining: decimal.MaxValue,
            ClipsSynthesized: 0,
            CacheHits: 0,
            ResetsUtc: monthStart.AddMonths(1));
        return Task.FromResult(Result.Ok(status));
    }

    public Task<Result> RecordAsync(UsageEntry entry, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Ok());
    }
}
