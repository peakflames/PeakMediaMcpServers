namespace PeakMediaRemoteMcpServer.Tests.Narration;

/// <summary>
/// The one test that talks to the real OpenRouter (about $0.001 a run). Skipped unless
/// PEAK_MEDIA_LIVE_OPENROUTER_API_KEY is set; run it with <c>build.py test-live</c>.
/// </summary>
public class LiveOpenRouterTests
{
    private const string Model = "google/gemini-3.8-flash-lite-tts";
    private const string Voice = "Rasalgethi";

    // Live evidence for TOR-03-IyOofVh, TOR-03-eKeTzs6, TOR-04-DYooFQF (Verified, 8po1tRg) and
    // TOR-05-MUV56KT, TOR-04-2qCsalR (Planned, EYGrros): a real clip is synthesized as 24 kHz 16-bit mono
    // WAV, downloadable from its url, its real cost is read from OpenRouter's generation statistics, and
    // repeating the call is served from the cache for free. This test cites; it does not mark them.
    [LiveOpenRouterFact]
    [Trait("Category", "LiveOpenRouter")]
    public async Task Real_openrouter_round_trip_reads_cost_and_caches()
    {
        await using var client = await NarrationClient.StartLiveAsync(LiveOpenRouterFactAttribute.Key);
        var clips = new[] { NarrationClient.Clip(1, "This is a live gateway check.") };

        var first = await client.SynthesizeAsync(clips, model: Model, voice: Voice);

        var entry = Assert.Single(NarrationClient.Entries(first.Text));
        Assert.False(entry.GetProperty("cached").GetBoolean(), first.Text);
        var durationMs = entry.GetProperty("durationMs").GetInt32();
        Assert.True(durationMs > 0, first.Text);

        using var download = await client.DownloadAsync(entry.GetProperty("url").GetString()!);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("audio/wav", download.Content.Headers.ContentType?.MediaType);
        var wav = await download.Content.ReadAsByteArrayAsync();
        Assert.True(wav.Length > 44, "the WAV has no data");
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));
        Assert.Equal(24000, BitConverter.ToInt32(wav, 24));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
        var dataLength = BitConverter.ToInt32(wav, 40);
        Assert.True(dataLength > 0, "the WAV data chunk is empty");
        Assert.Equal((int)Math.Round(dataLength / 48.0, MidpointRounding.AwayFromZero), durationMs);

        var cost = entry.GetProperty("costUsd").GetDecimal();
        Assert.True(cost > 0m, $"costUsd was {cost}: the generation cost lookup did not read OpenRouter's total_cost");

        var second = await client.SynthesizeAsync(clips, model: Model, voice: Voice);

        var again = Assert.Single(NarrationClient.Entries(second.Text));
        Assert.True(again.GetProperty("cached").GetBoolean(), second.Text);
        Assert.Equal(0m, again.GetProperty("costUsd").GetDecimal());
        Assert.Contains(
            second.Text.Split('\n').Select(l => l.TrimEnd('\r')),
            l => l.StartsWith("1 clips · 1 cached · $0.0000 this call", StringComparison.Ordinal));
    }
}
