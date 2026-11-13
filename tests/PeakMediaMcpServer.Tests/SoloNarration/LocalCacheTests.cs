namespace PeakMediaMcpServer.Tests.SoloNarration;

[Collection("StdioProcess")]
public class LocalCacheTests
{
    // TOR-06-heFgF6C: with PEAK_MEDIA_TTS_CACHE unset and home H, each clip lands at
    // H/.cache/peak-media/tts/<cacheKey>; entries carry path (no url) and the cost the fake reported.
    [Fact]
    public async Task TOR_06_heFgF6C_clips_written_to_home_cache_with_path_and_reported_cost()
    {
        await using var fake = await FakeOpenRouter.StartListeningAsync();
        fake.CostFor = _ => 0.0123m;
        using var client = await StdioClient.StartAsync(fake.BaseUrl!, new Dictionary<string, string?> { ["PEAK_MEDIA_TTS_CACHE"] = null });

        var result = await client.SynthesizeAsync(StdioClient.Clips(2));

        var entries = StdioClient.Entries(result.Text);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry =>
        {
            var key = entry.GetProperty("cacheKey").GetString()!;
            var path = entry.GetProperty("path").GetString()!;
            Assert.Equal($"{client.Home.Path}/.cache/peak-media/tts/{key}", path);
            Assert.False(entry.TryGetProperty("url", out _));
            Assert.True(StdioClient.IsValidWav(path), $"no valid 16-bit mono 24 kHz WAV at {path}");
            Assert.Equal(0.0123m, entry.GetProperty("costUsd").GetDecimal());
            Assert.False(entry.GetProperty("cached").GetBoolean());
        });
    }

    // TOR-06-vtaQIaK: PEAK_MEDIA_TTS_CACHE names the directory clips are written to.
    [Fact]
    public async Task TOR_06_vtaQIaK_clips_written_to_PEAK_MEDIA_TTS_CACHE()
    {
        using var cache = new TempDirectory();
        var directory = Path.Combine(cache.Path, "pm-cache");
        await using var fake = await FakeOpenRouter.StartListeningAsync();
        using var client = await StdioClient.StartAsync(fake.BaseUrl!, new Dictionary<string, string?> { ["PEAK_MEDIA_TTS_CACHE"] = directory });

        var result = await client.SynthesizeAsync(StdioClient.Clips(1));

        var entry = Assert.Single(StdioClient.Entries(result.Text));
        var path = entry.GetProperty("path").GetString()!;
        Assert.Equal($"{directory}/{entry.GetProperty("cacheKey").GetString()}", path);
        Assert.True(File.Exists(path));
        Assert.True(StdioClient.IsValidWav(path));
    }

    // TOR-06-D8tj7pH: a valid WAV already in the cache under the clip's key is served free, with no OpenRouter call.
    [Fact]
    public async Task TOR_06_D8tj7pH_cached_clip_is_served_without_calling_openrouter()
    {
        await using var fake = await FakeOpenRouter.StartListeningAsync();
        using var client = await StdioClient.StartAsync(fake.BaseUrl!);
        var text = StdioClient.TextOf(1);
        var key = ClipCacheKey.For("google/gemini-3.8-flash-lite-tts", "Rasalgethi", string.Empty, text);
        Directory.CreateDirectory(client.DefaultCacheDirectory);
        await File.WriteAllBytesAsync(Path.Combine(client.DefaultCacheDirectory, key), WavAudio.FromPcm(new byte[96_000]));

        var result = await client.SynthesizeAsync([StdioClient.Clip(1, text)]);

        var entry = Assert.Single(StdioClient.Entries(result.Text));
        Assert.Equal(key, entry.GetProperty("cacheKey").GetString());
        Assert.True(entry.GetProperty("cached").GetBoolean());
        Assert.Equal(0m, entry.GetProperty("costUsd").GetDecimal());
        Assert.Equal(2000, entry.GetProperty("durationMs").GetInt32());
        Assert.Empty(fake.Requests);
    }

    // A truncated file under the clip's key is not a hit: the clip is synthesized again and the file replaced.
    [Fact]
    public async Task Truncated_cached_file_is_synthesized_again()
    {
        await using var fake = await FakeOpenRouter.StartListeningAsync();
        using var client = await StdioClient.StartAsync(fake.BaseUrl!);
        var text = StdioClient.TextOf(1);
        var key = ClipCacheKey.For("google/gemini-3.8-flash-lite-tts", "Rasalgethi", string.Empty, text);
        Directory.CreateDirectory(client.DefaultCacheDirectory);
        var path = Path.Combine(client.DefaultCacheDirectory, key);
        await File.WriteAllBytesAsync(path, WavAudio.FromPcm(new byte[96_000])[..1000]);

        var result = await client.SynthesizeAsync([StdioClient.Clip(1, text)]);

        var entry = Assert.Single(StdioClient.Entries(result.Text));
        Assert.False(entry.GetProperty("cached").GetBoolean());
        Assert.Single(fake.Requests);
        Assert.True(StdioClient.IsValidWav(path));
    }
}
