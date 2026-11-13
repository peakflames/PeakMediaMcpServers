namespace PeakMediaRemoteMcpServer.Usage;

/// <summary>
/// The narration tools' view of usage: bound to the signed-in caller resolved from the validated access
/// token (<see cref="Caller.From"/>), never from anything a request can assert, so a tool only ever
/// reads and charges the caller's own records.
/// </summary>
public sealed class UsageLedger(UsageStore usage, QuotaStore quotas, IHttpContextAccessor httpContext, TimeProvider clock) : IUsageLedger
{
    public async Task<Result<UsageStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var caller = CurrentCaller();
        if (caller.IsFailed)
        {
            return Result.Fail<UsageStatus>(caller.Errors);
        }

        var month = UsageMonth.Current(clock);
        var totals = await usage.GetTotalsAsync(caller.Value, caller.Value.Subject, month, cancellationToken);
        if (totals.IsFailed)
        {
            return Result.Fail<UsageStatus>(totals.Errors);
        }

        var quota = await quotas.GetQuotaAsync(caller.Value, caller.Value.Subject, cancellationToken);
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

    public async Task<Result> RecordAsync(UsageEntry entry, CancellationToken cancellationToken)
    {
        var caller = CurrentCaller();
        if (caller.IsFailed)
        {
            return Result.Fail(caller.Errors);
        }

        return await usage.RecordAsync(caller.Value, caller.Value.Subject, entry, cancellationToken);
    }

    private Result<Caller> CurrentCaller()
    {
        var user = httpContext.HttpContext?.User;
        return user is null ? Result.Fail<Caller>("the caller is not signed in") : Caller.From(user);
    }
}
