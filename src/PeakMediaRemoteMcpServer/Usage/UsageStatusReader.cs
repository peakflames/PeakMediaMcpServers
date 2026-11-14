namespace PeakMediaRemoteMcpServer.Usage;

/// <summary>
/// One caller's standing for one month, read through <see cref="UsageStore"/> and <see cref="QuotaStore"/>
/// (so through <see cref="AccessRule"/>): own records only. The <c>my_usage</c> tool and the dashboard's
/// My Usage page both read here, so their figures cannot drift apart.
/// </summary>
public sealed class UsageStatusReader(UsageStore usage, QuotaStore quotas)
{
    public async Task<Result<UsageStatus>> ReadAsync(Caller caller, UsageMonth month, CancellationToken cancellationToken)
    {
        var totals = await usage.GetTotalsAsync(caller, caller.Subject, month, cancellationToken);
        if (totals.IsFailed)
        {
            return Result.Fail<UsageStatus>(totals.Errors);
        }

        var quota = await quotas.GetQuotaAsync(caller, caller.Subject, cancellationToken);
        if (quota.IsFailed)
        {
            return Result.Fail<UsageStatus>(quota.Errors);
        }

        var spent = totals.Value.Spent;
        return Result.Ok(new UsageStatus(
            month.Label,
            spent,
            quota.Value,
            Math.Max(0m, quota.Value - spent),
            totals.Value.ClipsSynthesized,
            totals.Value.CacheHits,
            month.ResetsUtc));
    }
}
