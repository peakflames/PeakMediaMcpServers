namespace PeakMediaRemoteMcpServer.Tests.Narration;

public class FailureRecoveryTests
{
    private const string SecretKey = "sk-or-test-SECRET-KEY-123";

    private static string TextOf(int n)
    {
        return $"Clip {n} says hello to everyone listening today.";
    }

    // TOR-03-u0NLjMV: the first request for clip 2 answers 503; the clip is retried once and the call succeeds.
    [Fact]
    public async Task TOR_03_u0NLjMV_clip_is_retried_once_after_a_503()
    {
        await using var client = await NarrationClient.StartAsync();
        var clip2Requests = 0;
        client.OpenRouter.StatusOverride = request =>
            request.Input == TextOf(2) && Interlocked.Increment(ref clip2Requests) == 1 ? 503 : null;

        var result = await client.SynthesizeAsync(NarrationClient.Clips(3));

        Assert.False(result.Text.StartsWith("ERROR:", StringComparison.Ordinal), result.Text);
        Assert.Equal(2, client.OpenRouter.RequestsFor(TextOf(2)));
        Assert.Equal(4, client.OpenRouter.Requests.Count);
    }

    // TOR-03-5607RO5: clip 5 never answers; after one retry the call fails naming clip 5 of 12 and stops.
    [Fact]
    public async Task TOR_03_5607RO5_timeout_names_the_clip_and_stops()
    {
        await using var client = await NarrationClient.StartAsync();
        client.OpenRouter.NoAnswer = request => request.Input == TextOf(5);

        var result = await client.SynthesizeAsync(NarrationClient.Clips(12));

        Assert.Equal(
            "ERROR: OpenRouter did not answer for clip 5 of 12 (timeout, retried once). Clips 1–4 are saved; run the same request again to continue.",
            result.Text);
        Assert.Equal(2, client.OpenRouter.RequestsFor(TextOf(5)));
        for (var n = 6; n <= 12; n++)
        {
            Assert.Equal(0, client.OpenRouter.RequestsFor(TextOf(n)));
        }
    }

    // TOR-03-FQcysYo: every request answers 402; the call reports out of credit after exactly one request.
    [Fact]
    public async Task TOR_03_FQcysYo_out_of_credit_is_not_retried()
    {
        await using var client = await NarrationClient.StartAsync();
        client.OpenRouter.StatusOverride = _ => 402;

        var result = await client.SynthesizeAsync(NarrationClient.Clips(1));

        Assert.Equal("ERROR: the gateway's OpenRouter account is out of credit. Ask an administrator to add credit.", result.Text);
        Assert.Single(client.OpenRouter.Requests);
    }

    // TOR-03-A312w2t (in-process half; SecretsInLogTests covers the gateway's log): every request answers 401;
    // the call reports a rejected key after one request and the result does not contain the key.
    [Fact]
    public async Task TOR_03_A312w2t_rejected_key_message_does_not_contain_key()
    {
        await using var client = await NarrationClient.StartAsync(overrides: new Dictionary<string, string?> { ["OpenRouter:ApiKey"] = SecretKey });
        client.OpenRouter.StatusOverride = _ => 401;

        var result = await client.SynthesizeAsync(NarrationClient.Clips(1));

        Assert.Equal("ERROR: the gateway's OpenRouter key was rejected. Ask an administrator to check the gateway's key.", result.Text);
        Assert.DoesNotContain(SecretKey, result.RawBody, StringComparison.Ordinal);
        var request = Assert.Single(client.OpenRouter.Requests);
        Assert.Equal($"Bearer {SecretKey}", request.Authorization);
    }

    // TOR-04-PXIafcT: a 12-clip call failed at clip 5; the same call again is served 1–4 from the cache and
    // pays only for clips 5–12.
    [Fact]
    public async Task TOR_04_PXIafcT_retry_pays_only_for_missing_clips()
    {
        await using var client = await NarrationClient.StartAsync();
        client.OpenRouter.StatusOverride = request => request.Input == TextOf(5) ? 503 : null;
        var failed = await client.SynthesizeAsync(NarrationClient.Clips(12));
        Assert.StartsWith("ERROR: OpenRouter did not answer for clip 5 of 12 (status 503, retried once). Clips 1–4 are saved", failed.Text, StringComparison.Ordinal);

        client.OpenRouter.StatusOverride = _ => null;
        var before = client.OpenRouter.Requests.Count;
        var result = await client.SynthesizeAsync(NarrationClient.Clips(12));

        var entries = NarrationClient.Entries(result.Text);
        Assert.Equal(12, entries.Count);
        for (var i = 0; i < 4; i++)
        {
            Assert.True(entries[i].GetProperty("cached").GetBoolean(), $"entry {i + 1} should be cached");
            Assert.Equal(0m, entries[i].GetProperty("costUsd").GetDecimal());
        }

        for (var i = 4; i < 12; i++)
        {
            Assert.False(entries[i].GetProperty("cached").GetBoolean(), $"entry {i + 1} should not be cached");
        }

        Assert.Equal(8, client.OpenRouter.Requests.Count - before);
    }

    [Theory]
    [InlineData(SpeechFailureKind.Unreachable, null, 3, 1, "ERROR: OpenRouter did not answer for clip 2 of 3 (unreachable, retried once). Clip 1 is saved; run the same request again to continue.")]
    [InlineData(SpeechFailureKind.ServerError, 502, 1, 0, "ERROR: OpenRouter did not answer for clip 1 of 1 (status 502, retried once). Run the same request again to retry.")]
    [InlineData(SpeechFailureKind.Other, 400, 3, 2, "ERROR: OpenRouter could not synthesize clip 3. Try the same call again in a moment; clips already finished are cached and free.")]
    public void Failure_message_names_clip_and_saved_clips(SpeechFailureKind kind, int? status, int total, int saved, string expected)
    {
        var message = NarrationService.SpeechFailure(new SpeechError(kind, status, retried: true), saved + 1, saved + 1, total, saved);

        Assert.Equal(expected, message);
    }
}
