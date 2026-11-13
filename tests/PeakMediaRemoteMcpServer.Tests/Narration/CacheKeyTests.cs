namespace PeakMediaRemoteMcpServer.Tests.Narration;

public class CacheKeyTests
{
    private const string Gemini = "google/gemini-3.8-flash-lite-tts";

    // TOR-04-lRsfqBV: cacheKey = lowercase hex SHA-256 of "model\0voice\0style\0text" (UTF-8) + ".wav".
    [Fact]
    public async Task TOR_04_lRsfqBV_cache_key_is_sha256_of_model_voice_style_text()
    {
        await using var client = await NarrationClient.StartAsync();

        var result = await client.SynthesizeAsync([NarrationClient.Clip(1, "Hello there.")], model: Gemini, voice: "Rasalgethi", style: "Plain.");

        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{Gemini}\0Rasalgethi\0Plain.\0Hello there."))) + ".wav";
        var entry = Assert.Single(NarrationClient.Entries(result.Text));
        Assert.Equal(expected, entry.GetProperty("cacheKey").GetString());
    }

    // TOR-04-TgW6vsE: a different voice, style or text is a different, uncached clip.
    [Fact]
    public async Task TOR_04_TgW6vsE_any_difference_in_voice_style_or_text_is_uncached()
    {
        await using var client = await NarrationClient.StartAsync();
        var first = await client.SynthesizeAsync([NarrationClient.Clip(1, "Hello there.", style: "Plain.", voice: "Rasalgethi")], model: Gemini);
        var firstKey = Assert.Single(NarrationClient.Entries(first.Text)).GetProperty("cacheKey").GetString();

        var result = await client.SynthesizeAsync(
            [
                NarrationClient.Clip(1, "Hello there.", style: "Plain.", voice: "Kore"),
                NarrationClient.Clip(2, "Hello there.", style: "Excited.", voice: "Rasalgethi"),
                NarrationClient.Clip(3, "Hello there!", style: "Plain.", voice: "Rasalgethi"),
            ],
            model: Gemini);

        var entries = NarrationClient.Entries(result.Text);
        Assert.Equal(3, entries.Count);
        Assert.All(entries, e => Assert.False(e.GetProperty("cached").GetBoolean()));
        var keys = entries.Select(e => e.GetProperty("cacheKey").GetString()).ToList();
        Assert.Equal(3, keys.Distinct().Count());
        Assert.DoesNotContain(firstKey, keys);
    }
}
