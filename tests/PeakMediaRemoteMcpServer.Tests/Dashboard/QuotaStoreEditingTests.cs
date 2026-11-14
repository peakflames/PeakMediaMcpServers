namespace PeakMediaRemoteMcpServer.Tests.Dashboard;

/// <summary>The quota store's reads and writes behind User Detail's quota form, checked through my_usage.</summary>
public class QuotaStoreEditingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static QuotaStore Quotas(NarrationClient client)
    {
        return client.Host.Services.GetRequiredService<QuotaStore>();
    }

    // TOR-08-iUG0WAC: no override → the quota is the default $25.00, flagged as the default.
    [Fact]
    public async Task TOR_08_iUG0WAC_no_override_reads_the_default()
    {
        await using var client = await NarrationClient.StartAsync(now: Now);
        var subject = await UsageSeed.SubjectOfAsync(client.Host);

        var info = await Quotas(client).GetQuotaInfoAsync(UsageSeed.Admin, subject, CancellationToken.None);

        Assert.True(info.IsSuccess);
        Assert.Equal(new QuotaInfo(25.00m, IsDefault: true), info.Value);
    }

    // TOR-08-QNNE1fW: an administrator sets $60 → the next my_usage shows $60.00 and the quota is no longer the default.
    [Fact]
    public async Task TOR_08_QNNE1fW_set_override_applies_to_next_my_usage()
    {
        await using var client = await NarrationClient.StartAsync(now: Now);
        var subject = await UsageSeed.SubjectOfAsync(client.Host);

        var set = await Quotas(client).SetOverrideAsync(UsageSeed.Admin, subject, 60m);

        Assert.True(set.IsSuccess);
        Assert.Equal("$60.00", NarrationClient.UsageTable((await client.MyUsageAsync()).Text)["quota"]);
        Assert.False((await Quotas(client).GetQuotaInfoAsync(UsageSeed.Admin, subject, CancellationToken.None)).Value.IsDefault);
    }

    // TOR-08-6Vzjnrg: override $60 → clear → my_usage shows the default $25.00 again.
    [Fact]
    public async Task TOR_08_6Vzjnrg_clear_override_restores_the_default()
    {
        await using var client = await NarrationClient.StartAsync(now: Now);
        var subject = await UsageSeed.SubjectOfAsync(client.Host);
        await UsageSeed.SetQuotaAsync(client.Host, 60m);

        var cleared = await Quotas(client).ClearOverrideAsync(UsageSeed.Admin, subject);

        Assert.True(cleared.IsSuccess);
        Assert.Equal("$25.00", NarrationClient.UsageTable((await client.MyUsageAsync()).Text)["quota"]);
        Assert.True((await Quotas(client).GetQuotaInfoAsync(UsageSeed.Admin, subject, CancellationToken.None)).Value.IsDefault);
    }

    // TOR-09-RA5qSsd: the store itself refuses a negative quota; non-admin callers can neither set nor clear.
    [Fact]
    public async Task TOR_09_RA5qSsd_negative_and_non_admin_changes_are_refused()
    {
        await using var client = await NarrationClient.StartAsync(now: Now);
        var subject = await UsageSeed.SubjectOfAsync(client.Host);
        var pat = new Caller(subject, "pat@example.com", IsAdmin: false);
        await UsageSeed.SetQuotaAsync(client.Host, 60m);

        Assert.True((await Quotas(client).SetOverrideAsync(UsageSeed.Admin, subject, -5m)).IsFailed);
        Assert.True((await Quotas(client).SetOverrideAsync(pat, subject, 1000m)).IsFailed);
        Assert.True((await Quotas(client).ClearOverrideAsync(pat, subject)).IsFailed);
        Assert.Equal("$60.00", NarrationClient.UsageTable((await client.MyUsageAsync()).Text)["quota"]);
    }
}
