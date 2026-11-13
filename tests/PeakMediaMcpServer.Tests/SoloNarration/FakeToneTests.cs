namespace PeakMediaMcpServer.Tests.SoloNarration;

[Collection("StdioProcess")]
public class FakeToneTests
{
    // TOR-06-g9QibOG: model fake/tone synthesizes 2 clips offline: no ERROR, a valid WAV at each path, no OpenRouter request.
    [Fact]
    public async Task TOR_06_g9QibOG_fake_tone_synthesizes_without_openrouter()
    {
        await using var fake = await FakeOpenRouter.StartListeningAsync();
        using var client = await StdioClient.StartAsync(fake.BaseUrl!);

        var result = await client.SynthesizeAsync(StdioClient.Clips(2), model: "fake/tone");

        Assert.False(result.Text.StartsWith("ERROR:", StringComparison.Ordinal), result.Text);
        var entries = StdioClient.Entries(result.Text);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry =>
        {
            var path = entry.GetProperty("path").GetString()!;
            Assert.True(StdioClient.IsValidWav(path), $"no valid WAV at {path}");
            Assert.Equal(0m, entry.GetProperty("costUsd").GetDecimal());
            Assert.True(entry.GetProperty("durationMs").GetInt32() > 0);
        });
        Assert.Empty(fake.Requests);
    }
}
