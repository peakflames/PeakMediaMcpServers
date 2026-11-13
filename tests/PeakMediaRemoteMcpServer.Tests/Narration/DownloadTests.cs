namespace PeakMediaRemoteMcpServer.Tests.Narration;

public class DownloadTests
{
    // TOR-04-DYooFQF: the url serves the WAV without a bearer token at 1 and 14 minutes, byte-identical.
    [Fact]
    public async Task TOR_04_DYooFQF_url_serves_wav_without_token_for_15_minutes()
    {
        await using var client = await NarrationClient.StartAsync();
        var result = await client.SynthesizeAsync(NarrationClient.Clips(1));
        var url = Assert.Single(NarrationClient.Entries(result.Text)).GetProperty("url").GetString()!;

        client.Clock.Advance(TimeSpan.FromMinutes(1));
        using var first = await client.DownloadAsync(url);
        var firstBody = await first.Content.ReadAsByteArrayAsync();
        client.Clock.Advance(TimeSpan.FromMinutes(13));
        using var second = await client.DownloadAsync(url);
        var secondBody = await second.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("audio/wav", first.Content.Headers.ContentType?.MediaType);
        Assert.Equal("audio/wav", second.Content.Headers.ContentType?.MediaType);
        Assert.Equal(firstBody, secondBody);
    }

    // TOR-04-bIGlR3O: 16 minutes after the result the url answers 410 problem details.
    [Fact]
    public async Task TOR_04_bIGlR3O_expired_url_answers_410_problem_details()
    {
        await using var client = await NarrationClient.StartAsync();
        var result = await client.SynthesizeAsync(NarrationClient.Clips(1));
        var url = Assert.Single(NarrationClient.Entries(result.Text)).GetProperty("url").GetString()!;

        client.Clock.Advance(TimeSpan.FromMinutes(16));
        using var response = await client.DownloadAsync(url);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "This download link has expired. Call synthesize_narration again with the same request for a new link; cached clips are free.",
            body.RootElement.GetProperty("detail").GetString());
    }

    // TOR-04-SqAxPKp: two callers' URLs for the same cached clip differ, contain no cacheKey, and carry a
    // token of at least 32 URL-safe characters.
    [Fact]
    public async Task TOR_04_SqAxPKp_download_tokens_are_not_derived_from_the_clip()
    {
        await using var client = await NarrationClient.StartAsync("pat@example.com");
        var otherToken = await client.SignInAnotherAsync("sam@example.com");
        var clips = NarrationClient.Clips(1);

        var first = Assert.Single(NarrationClient.Entries((await client.SynthesizeAsync(clips)).Text));
        var second = Assert.Single(NarrationClient.Entries((await client.SynthesizeAsync(clips, accessToken: otherToken)).Text));

        Assert.True(second.GetProperty("cached").GetBoolean());
        var cacheKey = first.GetProperty("cacheKey").GetString()!;
        Assert.Equal(cacheKey, second.GetProperty("cacheKey").GetString());
        var urls = new[] { first.GetProperty("url").GetString()!, second.GetProperty("url").GetString()! };
        Assert.NotEqual(urls[0], urls[1]);
        foreach (var url in urls)
        {
            Assert.DoesNotContain(cacheKey, url, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(cacheKey[..64], url, StringComparison.OrdinalIgnoreCase);
            var token = new Uri(url).Segments[^1];
            Assert.Matches("^[A-Za-z0-9_-]{32,}$", token);
        }
    }

    // TOR-01-B0JJ3Jt: GET /clips/not-a-real-token answers 404 RFC 9457 problem details.
    [Fact]
    public async Task TOR_01_B0JJ3Jt_unknown_clip_link_answers_404_problem_details()
    {
        await using var host = await GatewayTestHost.StartAsync();

        using var response = await host.Client.GetAsync("/clips/not-a-real-token");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Not Found", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            "No clip matches this download link. Call synthesize_narration again for a new link.",
            body.RootElement.GetProperty("detail").GetString());
    }
}
