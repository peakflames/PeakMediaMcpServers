namespace PeakMediaRemoteMcpServer.Tests.Usage;

public class IsolationTests
{
    // TOR-02-Dc8Z0Ge: pat has synthesized 12 clips this month for $0.03; sam has made no calls. Sam's
    // my_usage shows spent $0.00 and 0 clips, and never mentions pat.
    [Fact]
    public async Task TOR_02_Dc8Z0Ge_usage_is_kept_under_each_callers_own_subject()
    {
        await using var client = await NarrationClient.StartAsync();
        client.OpenRouter.CostFor = _ => 0.0025m;
        var pat = await client.SynthesizeAsync(NarrationClient.Clips(12, "Pat"));
        Assert.Equal(12, NarrationClient.Entries(pat.Text).Count);
        Assert.Equal("$0.03", NarrationClient.UsageTable((await client.MyUsageAsync()).Text)["spent"]);
        var sam = await client.SignInAnotherAsync("sam@example.com");

        var result = await client.MyUsageAsync(sam);

        var usage = NarrationClient.UsageTable(result.Text);
        Assert.Equal("$0.00", usage["spent"]);
        Assert.Equal("0", usage["clips synthesized"]);
        Assert.Equal("0", usage["cache hits"]);
        Assert.DoesNotContain("pat@example.com", result.RawBody, StringComparison.Ordinal);
        var patSubject = await UsageSeed.SubjectOfAsync(client.Host, "pat@example.com");
        Assert.Equal(12L, await client.Host.CountRowsAsync("usage_log", patSubject));
        Assert.Equal(0L, await client.Host.CountRowsAsync("usage_log", await UsageSeed.SubjectOfAsync(client.Host, "sam@example.com")));
    }
}
