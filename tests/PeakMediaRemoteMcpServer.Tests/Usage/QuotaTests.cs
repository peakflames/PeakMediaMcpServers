namespace PeakMediaRemoteMcpServer.Tests.Usage;

public class QuotaTests
{
    private static readonly DateTimeOffset MidNovember = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    // TOR-05-3JuMxwL: with Quota:DefaultMonthlyUsd left at 25 and no override, my_usage shows quota $25.00.
    [Fact]
    public async Task TOR_05_3JuMxwL_default_quota_is_25_without_override()
    {
        await using var client = await NarrationClient.StartAsync();
        Assert.Equal(25m, client.Host.Services.GetRequiredService<QuotaStore>().DefaultMonthlyUsd);

        var usage = NarrationClient.UsageTable((await client.MyUsageAsync()).Text);

        Assert.Equal("$25.00", usage["quota"]);
    }

    // TOR-05-3cXUiMQ: $24.80 spent of $25.00; the call's clips cost $0.52 in total. Every clip is
    // synthesized and the result notes that the caller is now over quota at $25.32 of $25.00.
    [Fact]
    public async Task TOR_05_3cXUiMQ_call_started_under_quota_completes_and_notes_overage()
    {
        await using var client = await NarrationClient.StartAsync();
        await UsageSeed.SpendAsync(client.Host, 24.80m);
        client.OpenRouter.CostFor = _ => 0.13m;

        var result = await client.SynthesizeAsync(NarrationClient.Clips(4));

        Assert.False(result.Text.StartsWith("ERROR:", StringComparison.Ordinal), result.Text);
        var entries = NarrationClient.Entries(result.Text);
        Assert.Equal(4, entries.Count);
        Assert.All(entries, e => Assert.False(e.GetProperty("cached").GetBoolean()));
        Assert.Equal(0.52m, entries.Sum(e => e.GetProperty("costUsd").GetDecimal()));
        Assert.Equal(4, client.OpenRouter.Requests.Count);
        Assert.Contains("You are now over your monthly quota ($25.32 of $25.00).", result.Text, StringComparison.Ordinal);
    }

    // TOR-05-N0KTSFQ: at 2026-11-12 10:00 UTC with $25.32 spent of $25.00, the call is refused with the
    // exact text and the fake OpenRouter receives no request.
    [Fact]
    public async Task TOR_05_N0KTSFQ_over_quota_call_is_refused_before_any_synthesis()
    {
        await using var client = await NarrationClient.StartAsync(now: MidNovember);
        await UsageSeed.SpendAsync(client.Host, 25.32m);

        var result = await client.SynthesizeAsync(NarrationClient.Clips(1));

        Assert.Equal(
            "ERROR: monthly quota of $25.00 reached ($25.32 spent this month). Try again after 2026-12-01 00:00 UTC or ask an administrator to raise your quota.",
            result.Text);
        Assert.Empty(client.OpenRouter.Requests);
        Assert.Equal(0, client.OpenRouter.GenerationLookups);
    }

    // TOR-05-HqJn98D: spend exactly equal to the quota is refused too.
    [Fact]
    public async Task TOR_05_HqJn98D_spend_equal_to_quota_is_refused()
    {
        await using var client = await NarrationClient.StartAsync(now: MidNovember);
        await UsageSeed.SpendAsync(client.Host, 25.00m, clips: 3);

        var result = await client.SynthesizeAsync(NarrationClient.Clips(1));

        Assert.Equal(
            "ERROR: monthly quota of $25.00 reached ($25.00 spent this month). Try again after 2026-12-01 00:00 UTC or ask an administrator to raise your quota.",
            result.Text);
        Assert.Empty(client.OpenRouter.Requests);
    }

    // TOR-05-zi52cgw: every clip of the call is cached, but the caller is over quota: still refused, no clip URL.
    [Fact]
    public async Task TOR_05_zi52cgw_over_quota_caller_is_refused_even_when_every_clip_is_cached()
    {
        await using var client = await NarrationClient.StartAsync();
        var first = await client.SynthesizeAsync(NarrationClient.Clips(3));
        Assert.Equal(3, NarrationClient.Entries(first.Text).Count);
        await UsageSeed.SpendAsync(client.Host, 25.32m);

        var result = await client.SynthesizeAsync(NarrationClient.Clips(3));

        Assert.StartsWith("ERROR: monthly quota of $25.00 reached", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("http", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(ClipEndpoints.PathPrefix, result.Text, StringComparison.Ordinal);
        Assert.Empty(NarrationClient.JsonBlocks(result.Text));
    }

    // TOR-05-efm0UOd: $25.32 spent during November; at 2026-12-01 00:00 UTC a new month has begun, so the
    // call is served and my_usage shows month 2026-12 with only that clip's cost.
    [Fact]
    public async Task TOR_05_efm0UOd_month_to_date_spend_resets_at_midnight_utc_on_the_first()
    {
        await using var client = await NarrationClient.StartAsync(now: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        await UsageSeed.SpendAsync(client.Host, 12.00m);
        client.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 30, 23, 59, 59, TimeSpan.Zero));
        await UsageSeed.SpendAsync(client.Host, 13.32m);
        Assert.Equal("$25.32", NarrationClient.UsageTable((await client.MyUsageAsync()).Text)["spent"]);
        client.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
        client.OpenRouter.CostFor = _ => 0.05m;

        var result = await client.SynthesizeAsync(NarrationClient.Clips(1));

        Assert.False(result.Text.StartsWith("ERROR:", StringComparison.Ordinal), result.Text);
        var cost = Assert.Single(NarrationClient.Entries(result.Text)).GetProperty("costUsd").GetDecimal();
        var usage = NarrationClient.UsageTable((await client.MyUsageAsync()).Text);
        Assert.Equal("2026-12", usage["month"]);
        Assert.Equal(UsageText.Dollars(cost), usage["spent"]);
        Assert.Equal("$0.05", usage["spent"]);
        Assert.Equal("1", usage["clips synthesized"]);
    }

    // TOR-05-babYeNE: $25.32 spent of the default $25.00; an administrator then sets the quota to $60.00
    // through the quota store — the next call is served and my_usage shows quota $60.00.
    [Fact]
    public async Task TOR_05_babYeNE_admin_override_applies_to_the_next_call()
    {
        await using var client = await NarrationClient.StartAsync();
        await UsageSeed.SpendAsync(client.Host, 25.32m);
        Assert.StartsWith("ERROR: monthly quota of $25.00 reached", (await client.SynthesizeAsync(NarrationClient.Clips(1))).Text, StringComparison.Ordinal);

        await UsageSeed.SetQuotaAsync(client.Host, 60m);
        var result = await client.SynthesizeAsync(NarrationClient.Clips(1));

        Assert.False(result.Text.StartsWith("ERROR:", StringComparison.Ordinal), result.Text);
        Assert.Equal("$60.00", NarrationClient.UsageTable((await client.MyUsageAsync()).Text)["quota"]);
    }

    // Access rule (TOR-02-Dc8Z0Ge): a non-admin caller can neither set a quota override nor read or write
    // another caller's usage or quota; an administrator can read anyone's.
    [Fact]
    public async Task Non_admin_cannot_set_overrides_or_reach_another_callers_records()
    {
        await using var client = await NarrationClient.StartAsync();
        await client.SignInAnotherAsync("sam@example.com");
        var pat = await UsageSeed.SubjectOfAsync(client.Host, "pat@example.com");
        var sam = await UsageSeed.SubjectOfAsync(client.Host, "sam@example.com");
        var samCaller = new Caller(sam, "sam@example.com", IsAdmin: false);
        var quotas = client.Host.Services.GetRequiredService<QuotaStore>();
        var usage = client.Host.Services.GetRequiredService<UsageStore>();
        var month = UsageMonth.Current(client.Clock);

        Assert.True((await quotas.SetOverrideAsync(samCaller, sam, 1000m)).IsFailed);
        Assert.True((await quotas.SetOverrideAsync(samCaller, pat, 0m)).IsFailed);
        Assert.True((await quotas.GetQuotaAsync(samCaller, pat, CancellationToken.None)).IsFailed);
        Assert.True((await usage.GetTotalsAsync(samCaller, pat, month, CancellationToken.None)).IsFailed);
        Assert.True((await usage.RecordAsync(samCaller, pat, new UsageEntry(UsageSeed.Model, "Kore", 1, 1m, "k.wav", false), CancellationToken.None)).IsFailed);
        Assert.Equal(0L, await client.Host.CountRowsAsync("quota_overrides"));
        Assert.Equal(0L, await client.Host.CountRowsAsync("usage_log"));

        Assert.True((await quotas.GetQuotaAsync(UsageSeed.Admin, pat, CancellationToken.None)).IsSuccess);
        Assert.True((await usage.GetTotalsAsync(UsageSeed.Admin, pat, month, CancellationToken.None)).IsSuccess);
    }
}
