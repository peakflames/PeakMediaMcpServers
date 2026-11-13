namespace PeakMediaRemoteMcpServer.Tests.Narration;

/// <summary>Every invalid request is refused before any spend: exact ERROR text, zero upstream requests.</summary>
public class ValidationTests
{
    private const string Gemini = "google/gemini-3.8-flash-lite-tts";

    // TOR-03-nt9zDad: an unknown model is refused before synthesis.
    [Fact]
    public async Task TOR_03_nt9zDad_unknown_model_is_refused()
    {
        await using var client = await NarrationClient.StartAsync();

        var result = await client.SynthesizeAsync(NarrationClient.Clips(1), model: "acme/unknown-tts");

        Assert.Equal("ERROR: unknown model 'acme/unknown-tts'. Call list_voices and pick one of its models.", result.Text);
        Assert.Empty(client.OpenRouter.Requests);
    }

    // TOR-03-mPpT1yR: a voice the model does not have is refused before synthesis.
    [Fact]
    public async Task TOR_03_mPpT1yR_unknown_voice_is_refused()
    {
        await using var client = await NarrationClient.StartAsync();

        var result = await client.SynthesizeAsync(NarrationClient.Clips(1), model: Gemini, voice: "Rasalgeti");

        Assert.Equal(
            "ERROR: unknown voice 'Rasalgeti' for google/gemini-3.8-flash-lite-tts. Call list_voices and pick one of its Gemini voices.",
            result.Text);
        Assert.Empty(client.OpenRouter.Requests);
    }

    // TOR-03-HLJlVSo: voice names match case-insensitively and go upstream in catalogue casing.
    [Fact]
    public async Task TOR_03_HLJlVSo_voice_matches_case_insensitively()
    {
        await using var client = await NarrationClient.StartAsync();

        var result = await client.SynthesizeAsync(NarrationClient.Clips(1), model: Gemini, voice: "rasalgethi");

        Assert.False(result.Text.StartsWith("ERROR:", StringComparison.Ordinal), result.Text);
        var request = Assert.Single(client.OpenRouter.Requests);
        Assert.Equal("Rasalgethi", request.Voice);
    }

    // TOR-03-IcVxy00: a clip whose text is blank is refused before synthesis.
    [Fact]
    public async Task TOR_03_IcVxy00_blank_clip_text_is_refused()
    {
        await using var client = await NarrationClient.StartAsync();
        object[] clips = [NarrationClient.Clip(1, "First."), NarrationClient.Clip(2, "   "), NarrationClient.Clip(3, "Third.")];

        var result = await client.SynthesizeAsync(clips);

        Assert.Equal("ERROR: clip 2 has no text. Give every clip between 1 and 4,000 characters of text.", result.Text);
        Assert.Empty(client.OpenRouter.Requests);
    }

    // TOR-03-eyAaGVV: more than 40 clips is refused before synthesis.
    [Fact]
    public async Task TOR_03_eyAaGVV_more_than_40_clips_is_refused()
    {
        await using var client = await NarrationClient.StartAsync();

        var result = await client.SynthesizeAsync(NarrationClient.Clips(41));

        Assert.Equal("ERROR: 41 clips were given and the limit is 40 per call. Split the script into calls of at most 40 clips.", result.Text);
        Assert.Empty(client.OpenRouter.Requests);
    }

    // TOR-03-LeqCLZu: a clip longer than 4,000 characters is refused before synthesis.
    [Fact]
    public async Task TOR_03_LeqCLZu_clip_over_4000_characters_is_refused()
    {
        await using var client = await NarrationClient.StartAsync();
        object[] clips = [NarrationClient.Clip(1, "First."), NarrationClient.Clip(2, "Second."), NarrationClient.Clip(3, new string('a', 4001))];

        var result = await client.SynthesizeAsync(clips);

        Assert.Equal("ERROR: clip 3 has 4,001 characters and the limit is 4,000. Split it into shorter clips.", result.Text);
        Assert.Empty(client.OpenRouter.Requests);
    }

    // TOR-03-9ruVWBA: 40 clips with clip 1 at exactly 4,000 characters is accepted.
    [Fact]
    public async Task TOR_03_9ruVWBA_limits_of_40_clips_and_4000_characters_are_accepted()
    {
        await using var client = await NarrationClient.StartAsync();
        var words = string.Join(' ', Enumerable.Repeat("word", 800));
        var longText = (words + new string('x', 4000)).Substring(0, 4000);
        Assert.Equal(4000, longText.Length);
        var clips = NarrationClient.Clips(40);
        clips[0] = NarrationClient.Clip(1, longText);

        var result = await client.SynthesizeAsync(clips);

        Assert.False(result.Text.StartsWith("ERROR:", StringComparison.Ordinal), result.Text);
        Assert.Equal(40, NarrationClient.Entries(result.Text).Count);
    }

    // TOR-03-uMFP7Oc: the model "fake/tone" is refused.
    [Fact]
    public async Task TOR_03_uMFP7Oc_fake_tone_model_is_refused()
    {
        await using var client = await NarrationClient.StartAsync();

        var result = await client.SynthesizeAsync(NarrationClient.Clips(1), model: "fake/tone");

        Assert.Equal("ERROR: unknown model 'fake/tone'. Call list_voices and pick one of its models.", result.Text);
        Assert.Empty(client.OpenRouter.Requests);
    }

    // TOR-01-Ji4u6Ta: a tool failure is a result whose text starts with ERROR: and names problem and next
    // action, with no exception type name or stack trace.
    [Fact]
    public async Task TOR_01_Ji4u6Ta_empty_clips_list_returns_error_text_without_exception_details()
    {
        await using var client = await NarrationClient.StartAsync();

        var result = await client.SynthesizeAsync([]);

        Assert.Equal("ERROR: no clips were given. Send between 1 and 40 clips.", result.Text);
        Assert.DoesNotMatch(new Regex(@"\b\w+Exception\b|\bat \w+\.|\s+at\s+[\w.]+\(|--- End of"), result.Text);
        Assert.Empty(client.OpenRouter.Requests);
    }
}
