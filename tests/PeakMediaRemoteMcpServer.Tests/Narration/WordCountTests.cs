namespace PeakMediaRemoteMcpServer.Tests.Narration;

public class WordCountTests
{
    // TOR-03-0gjBH7R: mood tags are not counted as words.
    [Fact]
    public async Task TOR_03_0gjBH7R_mood_tags_are_not_counted_as_words()
    {
        await using var client = await NarrationClient.StartAsync();

        var result = await client.SynthesizeAsync([NarrationClient.Clip(1, "<Warm> Welcome back to the show.")]);

        var entry = Assert.Single(NarrationClient.Entries(result.Text));
        Assert.Equal(5, entry.GetProperty("words").GetInt32());
    }
}
