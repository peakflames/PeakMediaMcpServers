namespace PeakMediaRemoteMcpServer.Tests.Narration;

public class ResultTests
{
    // TOR-03-E04hqrD: exactly one fenced JSON block; each entry has exactly the nine keys; cacheKey shape.
    [Fact]
    public async Task TOR_03_E04hqrD_one_json_block_with_exact_keys()
    {
        await using var client = await NarrationClient.StartAsync();

        var result = await client.SynthesizeAsync(NarrationClient.Clips(2));

        Assert.Equal(2, Regex.Matches(result.Text, "```").Count);
        var entries = NarrationClient.Entries(result.Text);
        Assert.Equal(2, entries.Count);
        string[] expectedKeys = ["cacheKey", "cached", "costUsd", "durationMs", "n", "sampleRate", "url", "words", "wpm"];
        foreach (var entry in entries)
        {
            Assert.Equal(expectedKeys, entry.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.Matches("^[0-9a-f]{64}\\.wav$", entry.GetProperty("cacheKey").GetString());
        }
    }

    // TOR-03-RevSWV4: the summary line names clip count, cached count, the call's cost and median wpm.
    [Fact]
    public async Task TOR_03_RevSWV4_summary_line_with_counts_cost_and_median_wpm()
    {
        await using var client = await NarrationClient.StartAsync();
        client.OpenRouter.WpmFor = _ => 127;
        var clips = NarrationClient.Clips(12);
        await client.SynthesizeAsync(clips[..3]);
        client.OpenRouter.CostFor = text => text.StartsWith("Clip 12 ", StringComparison.Ordinal) ? 0.0036m : 0.0031m;

        var result = await client.SynthesizeAsync(clips);

        Assert.Contains("12 clips · 3 cached · $0.0284 this call · median 127 wpm", result.Text.Split('\n').Select(l => l.TrimEnd('\r')));
    }

    // TOR-03-J5FXU3j: no audio bytes — text content only, under 20,000 characters, no base64 run of 200+.
    [Fact]
    public async Task TOR_03_J5FXU3j_result_carries_no_audio_bytes()
    {
        await using var client = await NarrationClient.StartAsync();

        var result = await client.SynthesizeAsync(NarrationClient.Clips(12));

        Assert.NotEmpty(result.ContentTypes);
        Assert.All(result.ContentTypes, type => Assert.Equal("text", type));
        Assert.True(result.Text.Length < 20000, $"result is {result.Text.Length} characters");
        Assert.DoesNotMatch("[A-Za-z0-9+/=_-]{200,}", result.Text);
    }
}
