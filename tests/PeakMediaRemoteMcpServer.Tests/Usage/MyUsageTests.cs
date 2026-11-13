namespace PeakMediaRemoteMcpServer.Tests.Usage;

public class MyUsageTests
{
    // TOR-05-bLVMjT4: at 2026-11-12 10:00 UTC, with a $25.00 quota, 418 synthesized clips costing $25.32
    // and 95 cache hits this month, my_usage shows every field.
    [Fact]
    public async Task TOR_05_bLVMjT4_reports_month_spend_quota_remaining_clips_hits_and_reset()
    {
        await using var client = await NarrationClient.StartAsync(now: new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero));
        await UsageSeed.SpendAsync(client.Host, 25.32m, clips: 418, cacheHits: 95);

        var result = await client.MyUsageAsync();

        Assert.False(result.Text.StartsWith("ERROR:", StringComparison.Ordinal), result.Text);
        var expected = new Dictionary<string, string>
        {
            ["month"] = "2026-11",
            ["spent"] = "$25.32",
            ["quota"] = "$25.00",
            ["remaining"] = "$0.00",
            ["clips synthesized"] = "418",
            ["cache hits"] = "95",
            ["resets"] = "2026-12-01 00:00 UTC",
        };
        Assert.Equal(expected, NarrationClient.UsageTable(result.Text));
    }

    // TOR-05-0pHv4Fb: $12.34 spent of $25.00 leaves $12.66.
    [Fact]
    public async Task TOR_05_0pHv4Fb_remaining_is_quota_minus_spend_to_the_cent()
    {
        await using var client = await NarrationClient.StartAsync();
        await UsageSeed.SpendAsync(client.Host, 12.34m, clips: 7);

        var usage = NarrationClient.UsageTable((await client.MyUsageAsync()).Text);

        Assert.Equal("$12.34", usage["spent"]);
        Assert.Equal("$25.00", usage["quota"]);
        Assert.Equal("$12.66", usage["remaining"]);
    }

    // TOR-05-WmxtWmi: a caller over quota still gets an answer from my_usage.
    [Fact]
    public async Task TOR_05_WmxtWmi_answers_when_over_quota()
    {
        await using var client = await NarrationClient.StartAsync();
        await UsageSeed.SpendAsync(client.Host, 25.32m);

        var result = await client.MyUsageAsync();

        Assert.False(result.Text.StartsWith("ERROR:", StringComparison.Ordinal), result.Text);
        Assert.Equal("$25.32", NarrationClient.UsageTable(result.Text)["spent"]);
    }
}
