namespace PeakMediaRemoteMcpServer.Tests.Narration;

public class ProgressTests
{
    // TOR-03-ehw1xk6: one progress notification per clip (1..12 of 12) before the result; cached and
    // synthesized clips have the specified messages.
    [Fact]
    public async Task TOR_03_ehw1xk6_one_progress_notification_per_clip()
    {
        await using var client = await NarrationClient.StartAsync();
        var clips = NarrationClient.Clips(12);
        await client.SynthesizeAsync(clips[..3]);

        var result = await client.SynthesizeAsync(clips, withProgress: true);

        Assert.Equal(12, result.Progress.Count);
        Assert.True(result.ProgressBeforeResult);
        Assert.Equal(Enumerable.Range(1, 12), result.Progress.Select(p => (int)p.GetProperty("progress").GetDouble()));
        Assert.All(result.Progress, p => Assert.Equal(12, (int)p.GetProperty("total").GetDouble()));
        Assert.All(result.Progress, p => Assert.Equal(NarrationClient.ProgressToken, p.GetProperty("progressToken").GetString()));
        Assert.Equal("clip 2/12 cached", result.Progress[1].GetProperty("message").GetString());
        Assert.Matches(@"^clip 4/12 synthesized \(\d+\.\d s audio, \$\d+\.\d{4}\)$", result.Progress[3].GetProperty("message").GetString());
    }
}
