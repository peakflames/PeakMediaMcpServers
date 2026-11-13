namespace PeakMediaMcpServer.Tests.SoloNarration;

[Collection("StdioProcess")]
public class SoloValidationTests
{
    // TOR-06-bPXmWnj: a misspelled voice is refused with the gateway's exact text, before any OpenRouter request.
    [Fact]
    public async Task TOR_06_bPXmWnj_unknown_voice_refused_with_gateway_text()
    {
        await using var fake = await FakeOpenRouter.StartListeningAsync();
        using var client = await StdioClient.StartAsync(fake.BaseUrl!);

        var result = await client.SynthesizeAsync(StdioClient.Clips(1), model: "google/gemini-3.8-flash-lite-tts", voice: "Rasalgeti");

        Assert.Equal("ERROR: unknown voice 'Rasalgeti' for google/gemini-3.8-flash-lite-tts. Call list_voices and pick one of its Gemini voices.", result.Text);
        Assert.Empty(fake.Requests);
    }
}
