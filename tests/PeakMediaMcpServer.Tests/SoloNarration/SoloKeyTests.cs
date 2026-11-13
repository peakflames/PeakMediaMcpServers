namespace PeakMediaMcpServer.Tests.SoloNarration;

[Collection("StdioProcess")]
public class SoloKeyTests
{
    // TOR-06-WYyXusz: with neither key variable set, the server exits 1, names the fix on stderr and
    // writes nothing to stdout.
    [Fact]
    public async Task TOR_06_WYyXusz_no_key_exits_1_naming_the_fix()
    {
        using var process = ServerProcess.Start(StdioClient.Project, [], new Dictionary<string, string?>
        {
            ["PEAK_MEDIA_OPENROUTER_API_KEY"] = null,
            ["TTS_OPENROUTER_API_KEY"] = null,
        });

        var exitCode = await process.WaitForExitAsync(StdioClient.Timeout);

        Assert.Contains("Error: PEAK_MEDIA_OPENROUTER_API_KEY is not set. Export your OpenRouter key and restart.", process.Stderr);
        Assert.Equal(1, exitCode);
        Assert.Empty(process.StdoutLines);
    }

    // TOR-06-dX6DFFO: only the legacy TTS_OPENROUTER_API_KEY is set; OpenRouter receives it as the Bearer token.
    [Fact]
    public async Task TOR_06_dX6DFFO_legacy_key_is_used_when_new_key_is_unset()
    {
        await using var fake = await FakeOpenRouter.StartListeningAsync();
        using var client = await StdioClient.StartAsync(fake.BaseUrl!, new Dictionary<string, string?>
        {
            ["PEAK_MEDIA_OPENROUTER_API_KEY"] = null,
            ["TTS_OPENROUTER_API_KEY"] = "sk-or-legacy-key",
        });

        var result = await client.SynthesizeAsync(StdioClient.Clips(1));

        Assert.DoesNotContain("ERROR:", result.Text, StringComparison.Ordinal);
        var request = Assert.Single(fake.Requests);
        Assert.Equal("Bearer sk-or-legacy-key", request.Authorization);
    }

    // TOR-06-ej048Ep: both are set; PEAK_MEDIA_OPENROUTER_API_KEY wins.
    [Fact]
    public async Task TOR_06_ej048Ep_new_key_wins_over_legacy_key()
    {
        await using var fake = await FakeOpenRouter.StartListeningAsync();
        using var client = await StdioClient.StartAsync(fake.BaseUrl!, new Dictionary<string, string?>
        {
            ["PEAK_MEDIA_OPENROUTER_API_KEY"] = "sk-or-new-key",
            ["TTS_OPENROUTER_API_KEY"] = "sk-or-legacy-key",
        });

        await client.SynthesizeAsync(StdioClient.Clips(1));

        var request = Assert.Single(fake.Requests);
        Assert.Equal("Bearer sk-or-new-key", request.Authorization);
    }

    // A rejected personal key names the user's own key, not the gateway's administrator.
    [Fact]
    public async Task Rejected_key_tells_the_solo_user_to_fix_their_own_key()
    {
        await using var fake = await FakeOpenRouter.StartListeningAsync();
        fake.StatusOverride = _ => 401;
        using var client = await StdioClient.StartAsync(fake.BaseUrl!);

        var result = await client.SynthesizeAsync(StdioClient.Clips(1));

        Assert.Equal("ERROR: your OpenRouter key was rejected. Check PEAK_MEDIA_OPENROUTER_API_KEY, export a valid key and restart the server.", result.Text);
        Assert.DoesNotContain(StdioClient.DefaultKey, result.RawResponse, StringComparison.Ordinal);
    }
}
