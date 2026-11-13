namespace PeakMediaRemoteMcpServer.Tests.Narration;

public class CostLookupTests
{
    // TOR-03-eKeTzs6 (regression): real OpenRouter answers 404 to the generation lookup for several seconds
    // after the speech call; the gateway must wait for it and record the real cost, not 0.
    [Fact]
    public async Task Cost_is_read_after_generation_answers_not_found_at_first()
    {
        await using var client = await NarrationClient.StartAsync();
        client.OpenRouter.GenerationNotReadyFor = 2;
        client.OpenRouter.CostFor = _ => 0.0042m;

        var result = await client.SynthesizeAsync(NarrationClient.Clips(3), withProgress: true);

        var entries = NarrationClient.Entries(result.Text);
        Assert.Equal(3, entries.Count);
        Assert.All(entries, e => Assert.Equal(0.0042m, e.GetProperty("costUsd").GetDecimal()));
        Assert.Equal([1, 2, 3], entries.Select(e => e.GetProperty("n").GetInt32()));
        Assert.Equal(Enumerable.Range(1, 3), result.Progress.Select(p => (int)p.GetProperty("progress").GetDouble()));
        Assert.All(result.Progress, p => Assert.EndsWith("$0.0042)", p.GetProperty("message").GetString(), StringComparison.Ordinal));
    }

    // A generation that never appears still yields the clip, recorded at cost 0, after the last retry.
    [Fact]
    public async Task Cost_is_zero_when_generation_never_appears()
    {
        await using var client = await NarrationClient.StartAsync();
        client.OpenRouter.GenerationNotReadyFor = int.MaxValue;

        var result = await client.SynthesizeAsync(NarrationClient.Clips(1));

        var entry = Assert.Single(NarrationClient.Entries(result.Text));
        Assert.False(entry.GetProperty("cached").GetBoolean());
        Assert.Equal(0m, entry.GetProperty("costUsd").GetDecimal());
        Assert.True(client.OpenRouter.GenerationLookups > 1, "the lookup should have been retried");
    }
}
