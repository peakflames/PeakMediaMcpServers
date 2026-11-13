namespace PeakMediaRemoteMcpServer.Tests.Narration;

public class OpenRouterRequestTests
{
    private const string Gemini = "google/gemini-3.8-flash-lite-tts";
    private const string Mai = "microsoft/mai-voice-2.1-flash";

    // TOR-03-UYCubxo: one POST to /api/v1/audio/speech with model, voice, response_format pcm, provider.zdr
    // true, carrying the gateway's key as a Bearer token.
    [Fact]
    public async Task TOR_03_UYCubxo_uncached_clip_is_requested_as_pcm_with_zero_retention()
    {
        await using var client = await NarrationClient.StartAsync();

        await client.SynthesizeAsync([NarrationClient.Clip(1, "A clip nobody has cached.")], model: Gemini, voice: "Rasalgethi");

        // The fake records only POSTs to /api/v1/audio/speech.
        var request = Assert.Single(client.OpenRouter.Requests);
        Assert.Equal(Gemini, request.Model);
        Assert.Equal("Rasalgethi", request.Voice);
        Assert.Equal("pcm", request.ResponseFormat);
        Assert.True(request.Zdr);
        Assert.Equal("Bearer test-key", request.Authorization);
    }

    // TOR-03-Ah5Slpu: the style goes to Gemini as "instructions"; MAI gets no "instructions" key.
    [Fact]
    public async Task TOR_03_Ah5Slpu_style_sent_to_gemini_only()
    {
        await using var client = await NarrationClient.StartAsync();

        await client.SynthesizeAsync([NarrationClient.Clip(1, "Styled for Gemini.")], model: Gemini, style: "Plain, measured delivery.");
        await client.SynthesizeAsync([NarrationClient.Clip(1, "Styled for MAI.")], model: Mai, style: "Plain, measured delivery.");

        var gemini = Assert.Single(client.OpenRouter.Requests, r => r.Model == Gemini);
        var mai = Assert.Single(client.OpenRouter.Requests, r => r.Model == Mai);
        Assert.Equal("Plain, measured delivery.", gemini.Instructions);
        Assert.False(mai.HasInstructions);
    }

    // TOR-03-JKfDgpt: a clip's own style replaces the call's style.
    [Fact]
    public async Task TOR_03_JKfDgpt_clip_style_overrides_call_style()
    {
        await using var client = await NarrationClient.StartAsync();

        await client.SynthesizeAsync(
            [NarrationClient.Clip(1, "The first clip."), NarrationClient.Clip(2, "The second clip.", style: "Excited.")],
            model: Gemini,
            style: "Plain.");

        Assert.Equal("Plain.", Assert.Single(client.OpenRouter.Requests, r => r.Input == "The first clip.").Instructions);
        Assert.Equal("Excited.", Assert.Single(client.OpenRouter.Requests, r => r.Input == "The second clip.").Instructions);
    }
}
