namespace PeakMediaRemoteMcpServer.Tests.Narration;

public class SpineTests
{
    // TOR-03-IyOofVh: a signed-in caller with no OpenRouter key narrates 12 uncached clips in one call: 12
    // entries in order, all cached:false with a url, and every url downloads a WAV.
    [Fact]
    public async Task TOR_03_IyOofVh_spine_of_12_clips_in_one_call_without_caller_key()
    {
        await using var client = await NarrationClient.StartAsync();

        var result = await client.SynthesizeAsync(
            NarrationClient.Clips(12),
            model: "google/gemini-3.8-flash-lite-tts",
            voice: "Rasalgethi",
            style: "Warm and unhurried.");

        var entries = NarrationClient.Entries(result.Text);
        Assert.Equal(Enumerable.Range(1, 12), entries.Select(e => e.GetProperty("n").GetInt32()));
        foreach (var entry in entries)
        {
            Assert.False(entry.GetProperty("cached").GetBoolean());
            using var response = await client.DownloadAsync(entry.GetProperty("url").GetString()!);
            var wav = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        }

        // The caller sent no OpenRouter key; every upstream request carried the gateway's own.
        Assert.Equal(12, client.OpenRouter.Requests.Count);
        Assert.All(client.OpenRouter.Requests, r => Assert.Equal("Bearer test-key", r.Authorization));
    }
}
