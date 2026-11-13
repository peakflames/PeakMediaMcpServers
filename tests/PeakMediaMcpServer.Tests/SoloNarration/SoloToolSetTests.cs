namespace PeakMediaMcpServer.Tests.SoloNarration;

[Collection("StdioProcess")]
public class SoloToolSetTests
{
    // TOR-06-emXCHe2: the stdio server offers exactly synthesize_narration and list_voices (no my_usage).
    [Fact]
    public async Task TOR_06_emXCHe2_offers_exactly_synthesize_narration_and_list_voices()
    {
        await using var fake = await FakeOpenRouter.StartListeningAsync();
        using var client = await StdioClient.StartAsync(fake.BaseUrl!);

        var tools = await client.ListToolsAsync();

        Assert.Equal(["list_voices", "synthesize_narration"], tools.Select(t => t.GetProperty("name").GetString()).Order());
    }

    // TOR-06-FXNq0Rx: list_voices names the four gateway models and fake/tone.
    [Fact]
    public async Task TOR_06_FXNq0Rx_list_voices_includes_gateway_models_and_fake_tone()
    {
        await using var fake = await FakeOpenRouter.StartListeningAsync();
        using var client = await StdioClient.StartAsync(fake.BaseUrl!);

        var result = await client.CallToolAsync("list_voices", new Dictionary<string, object>());

        string[] models = ["google/gemini-3.8-flash-lite-tts", "google/gemini-3.8-flash-tts", "microsoft/mai-voice-2.1-flash", "microsoft/mai-voice-2.1", "fake/tone"];
        Assert.All(models, model => Assert.Contains($"### {model}\n", result.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal));
    }

    // Drift guard for TOR-06-FXNq0Rx / TOR-06-PY6kH1r: the stdio catalogue is the gateway's catalogue plus fake/tone.
    [Fact]
    public void Stdio_catalogue_is_gateway_catalogue_plus_fake_tone()
    {
        var gateway = ModelIds(Path.Combine(RepoPaths.RepoRoot, "src", "PeakMediaRemoteMcpServer", "appsettings.json"));
        var stdio = ModelIds(Path.Combine(RepoPaths.RepoRoot, "src", "PeakMediaMcpServer", "appsettings.json"));

        Assert.Equal([.. gateway, "fake/tone"], stdio);
        Assert.Equal(DefaultModel(Path.Combine(RepoPaths.RepoRoot, "src", "PeakMediaRemoteMcpServer", "appsettings.json")),
            DefaultModel(Path.Combine(RepoPaths.RepoRoot, "src", "PeakMediaMcpServer", "appsettings.json")));
    }

    private static List<string> ModelIds(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return [.. document.RootElement.GetProperty("Tts").GetProperty("Models").EnumerateArray().Select(m => m.GetProperty("Id").GetString()!)];
    }

    private static string? DefaultModel(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("Tts").GetProperty("DefaultModel").GetString();
    }
}
