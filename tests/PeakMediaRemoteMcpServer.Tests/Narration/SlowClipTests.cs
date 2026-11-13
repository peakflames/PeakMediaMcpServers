namespace PeakMediaRemoteMcpServer.Tests.Narration;

public class SlowClipTests
{
    // TOR-03-SVUzauN: a clip below 70% of the call's median wpm gets a warning naming it, its wpm and the
    // median, saying a mood tag may have been read aloud; the other clips get none.
    [Fact]
    public async Task TOR_03_SVUzauN_slow_clip_is_warned_about()
    {
        await using var client = await NarrationClient.StartAsync();
        client.OpenRouter.WpmFor = text => text.StartsWith("Clip 5 ", StringComparison.Ordinal) ? 80 : 130;

        var result = await client.SynthesizeAsync(NarrationClient.Clips(5));

        var markdown = result.Text[..result.Text.IndexOf("```", StringComparison.Ordinal)];
        var warnings = markdown.Split('\n').Where(l => l.Contains("warning", StringComparison.OrdinalIgnoreCase)).ToList();
        var warning = Assert.Single(warnings);
        Assert.Contains("clip 5", warning, StringComparison.Ordinal);
        Assert.Contains("80 wpm", warning, StringComparison.Ordinal);
        Assert.Contains("median 130 wpm", warning, StringComparison.Ordinal);
        Assert.Contains("a mood tag may have been read aloud", warning, StringComparison.Ordinal);
        var withoutSummaryCount = markdown.Replace("5 clips", string.Empty, StringComparison.Ordinal);
        for (var n = 1; n <= 4; n++)
        {
            Assert.DoesNotMatch($@"\bclip {n}\b", withoutSummaryCount);
        }
    }
}
