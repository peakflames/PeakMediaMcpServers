namespace PeakMediaRemoteMcpServer.Tests.Usage;

public class CachedCallTests
{
    // TOR-04-csNgnyQ: pat synthesizes a clip; sam asks for the same model, voice, style and text and is
    // served from the cache at no cost, with no OpenRouter request; sam's my_usage shows $0.00 and 1 hit.
    [Fact]
    public async Task TOR_04_csNgnyQ_another_callers_clip_is_served_from_the_cache_for_free()
    {
        await using var client = await NarrationClient.StartAsync();
        var clips = new[] { NarrationClient.Clip(1, "Hello there.", style: "Plain.") };
        var first = await client.SynthesizeAsync(clips, voice: "Rasalgethi");
        Assert.False(Assert.Single(NarrationClient.Entries(first.Text)).GetProperty("cached").GetBoolean());
        var sam = await client.SignInAnotherAsync("sam@example.com");
        var before = client.OpenRouter.Requests.Count;

        var second = await client.SynthesizeAsync(clips, voice: "Rasalgethi", accessToken: sam);

        var entry = Assert.Single(NarrationClient.Entries(second.Text));
        Assert.True(entry.GetProperty("cached").GetBoolean());
        Assert.Equal(0m, entry.GetProperty("costUsd").GetDecimal());
        Assert.Equal(before, client.OpenRouter.Requests.Count);
        var usage = NarrationClient.UsageTable((await client.MyUsageAsync(sam)).Text);
        Assert.Equal("$0.00", usage["spent"]);
        Assert.Equal("1", usage["cache hits"]);
        Assert.Equal("0", usage["clips synthesized"]);
    }

    // TOR-04-2qCsalR: a 12-clip spine repeated unchanged is all cached: the summary reads
    // "12 clips · 12 cached · $0.00 this call", every progress message is "clip N/12 cached", OpenRouter
    // gets no request, and month-to-date spend is unchanged.
    [Fact]
    public async Task TOR_04_2qCsalR_repeated_spine_is_served_entirely_from_the_cache()
    {
        await using var client = await NarrationClient.StartAsync();
        client.OpenRouter.CostFor = _ => 0.0125m;
        await client.SynthesizeAsync(NarrationClient.Clips(12));
        var spentBefore = NarrationClient.UsageTable((await client.MyUsageAsync()).Text)["spent"];
        Assert.Equal("$0.15", spentBefore);
        var before = client.OpenRouter.Requests.Count;

        var result = await client.SynthesizeAsync(NarrationClient.Clips(12), withProgress: true);

        Assert.Contains(
            result.Text.Split('\n').Select(l => l.TrimEnd('\r')),
            l => l.StartsWith("12 clips · 12 cached · $0.00 this call", StringComparison.Ordinal));
        Assert.Contains("12 clips · 12 cached · $0.00 this call", result.Text, StringComparison.Ordinal);
        Assert.Equal(12, result.Progress.Count);
        Assert.All(result.Progress, p => Assert.Matches(@"^clip \d+/12 cached$", p.GetProperty("message").GetString()));
        Assert.Equal(before, client.OpenRouter.Requests.Count);
        Assert.Equal(spentBefore, NarrationClient.UsageTable((await client.MyUsageAsync()).Text)["spent"]);
    }

    // TOR-04-xYMUNYV: 12 clips whose download URLs have expired; the same call again returns every clip
    // cached with a new URL that downloads (200), and month-to-date spend is unchanged.
    [Fact]
    public async Task TOR_04_xYMUNYV_expired_urls_are_renewed_at_no_cost()
    {
        await using var client = await NarrationClient.StartAsync();
        client.OpenRouter.CostFor = _ => 0.0125m;
        var first = await client.SynthesizeAsync(NarrationClient.Clips(12));
        var oldUrls = NarrationClient.Entries(first.Text).Select(e => e.GetProperty("url").GetString()!).ToList();
        var spentBefore = NarrationClient.UsageTable((await client.MyUsageAsync()).Text)["spent"];
        client.Clock.Advance(TimeSpan.FromMinutes(16));
        using (var expired = await client.DownloadAsync(oldUrls[0]))
        {
            Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        }

        var second = await client.SynthesizeAsync(NarrationClient.Clips(12));

        var entries = NarrationClient.Entries(second.Text);
        Assert.Equal(12, entries.Count);
        for (var i = 0; i < 12; i++)
        {
            Assert.True(entries[i].GetProperty("cached").GetBoolean());
            var url = entries[i].GetProperty("url").GetString()!;
            Assert.NotEqual(oldUrls[i], url);
            using var download = await client.DownloadAsync(url);
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        }

        Assert.Equal(spentBefore, NarrationClient.UsageTable((await client.MyUsageAsync()).Text)["spent"]);
    }
}
