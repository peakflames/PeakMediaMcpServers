namespace PeakMediaMcpServer.Tests.SoloNarration;

[Collection("StdioProcess")]
public class UpstreamTests
{
    // TOR-06-UeA7aSw: the request to OpenRouter asks for PCM with zero data retention.
    [Fact]
    public async Task TOR_06_UeA7aSw_request_asks_for_pcm_and_zdr()
    {
        await using var fake = await FakeOpenRouter.StartListeningAsync();
        using var client = await StdioClient.StartAsync(fake.BaseUrl!);

        await client.SynthesizeAsync(StdioClient.Clips(1), model: "google/gemini-3.8-flash-lite-tts", voice: "Rasalgethi");

        var request = Assert.Single(fake.Requests);
        Assert.Equal("google/gemini-3.8-flash-lite-tts", request.Model);
        Assert.Equal("Rasalgethi", request.Voice);
        Assert.Equal("pcm", request.ResponseFormat);
        Assert.True(request.Zdr);
    }

    // TOR-06-i2lj5iF: with a progress token, 3 uncached clips give exactly 3 progress notifications (1..3 of 3) before the result.
    [Fact]
    public async Task TOR_06_i2lj5iF_one_progress_notification_per_clip_before_result()
    {
        await using var fake = await FakeOpenRouter.StartListeningAsync();
        using var client = await StdioClient.StartAsync(fake.BaseUrl!);

        var result = await client.SynthesizeAsync(StdioClient.Clips(3), withProgress: true);

        Assert.Equal(3, StdioClient.Entries(result.Text).Count);
        Assert.Equal(3, result.Progress.Count);
        Assert.Equal([1, 2, 3], result.Progress.Select(p => p.GetProperty("progress").GetInt32()));
        Assert.All(result.Progress, p => Assert.Equal(3, p.GetProperty("total").GetInt32()));
        Assert.All(result.Progress, p => Assert.Equal(StdioClient.ProgressToken, p.GetProperty("progressToken").GetString()));
        Assert.True(result.ProgressBeforeResult);
    }

    // TOR-06-fTgpl4m: clip 2 always answers 503; it is retried once, then the call fails naming clip 2, with clip 1 saved.
    [Fact]
    public async Task TOR_06_fTgpl4m_retries_once_then_fails_naming_the_clip()
    {
        await using var fake = await FakeOpenRouter.StartListeningAsync();
        fake.StatusOverride = request => request.Input == StdioClient.TextOf(2) ? 503 : null;
        using var client = await StdioClient.StartAsync(fake.BaseUrl!);

        var result = await client.SynthesizeAsync(StdioClient.Clips(3));

        Assert.Equal("ERROR: OpenRouter did not answer for clip 2 of 3 (HTTP 503, retried once). Clip 1 is saved; run the same request again to continue.", result.Text);
        Assert.Equal(2, fake.RequestsFor(StdioClient.TextOf(2)));
        Assert.Equal(0, fake.RequestsFor(StdioClient.TextOf(3)));
        var key1 = ClipCacheKey.For("google/gemini-3.8-flash-lite-tts", "Rasalgethi", string.Empty, StdioClient.TextOf(1));
        Assert.True(StdioClient.IsValidWav(Path.Combine(client.DefaultCacheDirectory, key1)));
    }
}
