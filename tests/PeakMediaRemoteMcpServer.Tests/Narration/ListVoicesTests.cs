namespace PeakMediaRemoteMcpServer.Tests.Narration;

public class ListVoicesTests
{
    // Tool inventory: a signed-in tools/list offers both narration tools.
    [Fact]
    public async Task Tools_list_offers_synthesize_narration_and_list_voices()
    {
        await using var client = await NarrationClient.StartAsync();

        var names = await client.ListToolNamesAsync();

        Assert.Contains("synthesize_narration", names);
        Assert.Contains("list_voices", names);
    }

    // TOR-03-Vaata1w: list_voices lists exactly the four allowed models with default voice and style-sent,
    // names the default model, lists at least one voice per model including its default, and never fake/tone.
    [Fact]
    public async Task TOR_03_Vaata1w_list_voices_lists_allowed_models_and_default()
    {
        await using var client = await NarrationClient.StartAsync();

        var result = await client.CallToolAsync("list_voices", new Dictionary<string, object>());

        var rows = Regex.Matches(result.Text, @"^\| (\S+) \| (\S+) \| (yes|no) \|$", RegexOptions.Multiline)
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value))
            .ToList();
        Assert.Equal(
            [
                ("google/gemini-3.8-flash-lite-tts", "Rasalgethi", "yes"),
                ("google/gemini-3.8-flash-tts", "Rasalgethi", "yes"),
                ("microsoft/mai-voice-2.1-flash", "en-GB-Emily", "no"),
                ("microsoft/mai-voice-2.1", "en-GB-Emily", "no"),
            ],
            rows);
        Assert.Contains("Default model: `google/gemini-3.8-flash-lite-tts`", result.Text, StringComparison.Ordinal);
        foreach (var (model, defaultVoice, _) in rows)
        {
            var voices = Regex.Match(result.Text, $@"### {Regex.Escape(model)}\n\nVoices: (.+)").Groups[1].Value.Split(", ");
            Assert.NotEmpty(voices);
            Assert.Contains(defaultVoice, voices);
        }

        Assert.DoesNotContain("fake/tone", result.Text, StringComparison.Ordinal);
        Assert.Empty(client.OpenRouter.Requests);
    }
}
