namespace PeakMediaRemoteMcpServer.Tests.Usage;

public class RestartPersistenceTests
{
    // TOR-01-65SaYiJ: a caller has spent $0.52 this month, an administrator set their quota to $60.00, and
    // clip K is cached. After the gateway stops and starts again on the same Storage:DatabasePath,
    // my_usage shows $0.52 and $60.00, and a call for K reports "cached": true.
    [Fact]
    public async Task TOR_01_65SaYiJ_usage_overrides_and_cache_survive_a_restart()
    {
        using var data = new TempDirectory();
        var overrides = new Dictionary<string, string?> { ["Storage:DatabasePath"] = data.File("peakmedia.db") };
        var clipK = new[] { NarrationClient.Clip(1, "The clip with cache key K.") };
        string cacheKey;

        await using (var first = await NarrationClient.StartAsync(overrides: overrides))
        {
            first.OpenRouter.CostFor = _ => 0.52m;
            var synthesized = await first.SynthesizeAsync(clipK);
            var entry = Assert.Single(NarrationClient.Entries(synthesized.Text));
            Assert.False(entry.GetProperty("cached").GetBoolean());
            cacheKey = entry.GetProperty("cacheKey").GetString()!;
            await UsageSeed.SetQuotaAsync(first.Host, 60m);
            var usage = NarrationClient.UsageTable((await first.MyUsageAsync()).Text);
            Assert.Equal("$0.52", usage["spent"]);
            Assert.Equal("$60.00", usage["quota"]);
        }

        await using var second = await NarrationClient.StartAsync(overrides: overrides);

        var after = NarrationClient.UsageTable((await second.MyUsageAsync()).Text);
        Assert.Equal("$0.52", after["spent"]);
        Assert.Equal("$60.00", after["quota"]);
        var again = Assert.Single(NarrationClient.Entries((await second.SynthesizeAsync(clipK)).Text));
        Assert.Equal(cacheKey, again.GetProperty("cacheKey").GetString());
        Assert.True(again.GetProperty("cached").GetBoolean());
        Assert.Empty(second.OpenRouter.Requests);
    }
}
