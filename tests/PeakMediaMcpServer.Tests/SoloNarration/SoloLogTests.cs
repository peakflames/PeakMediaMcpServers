namespace PeakMediaMcpServer.Tests.SoloNarration;

[Collection("StdioProcess")]
public class SoloLogTests
{
    private const string Key = "sk-or-test-SOLO-KEY-789";
    private const string Marker = "Zebra-quartz-lantern-7391";

    // TOR-06-GC17sBD: neither stderr nor any file under logs/ carries the narration text or the user's key.
    [Fact]
    public async Task TOR_06_GC17sBD_log_never_holds_narration_text_or_key()
    {
        await using var fake = await FakeOpenRouter.StartListeningAsync();
        using var client = await StdioClient.StartAsync(fake.BaseUrl!, new Dictionary<string, string?> { ["PEAK_MEDIA_OPENROUTER_API_KEY"] = Key });

        var result = await client.SynthesizeAsync([StdioClient.Clip(1, $"{Marker} is the marker sentence.")]);
        await client.StopAsync();

        Assert.Single(StdioClient.Entries(result.Text));
        Assert.Equal($"Bearer {Key}", Assert.Single(fake.Requests).Authorization);
        Assert.DoesNotContain(Marker, client.Process.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, client.Process.Stderr, StringComparison.Ordinal);
        var logs = RepoPaths.LogDirectory(StdioClient.Project);
        Assert.True(Directory.Exists(logs), $"no log directory at {logs}");
        foreach (var file in Directory.EnumerateFiles(logs, "*", SearchOption.AllDirectories))
        {
            var content = await ReadSharedAsync(file);
            Assert.DoesNotContain(Marker, content, StringComparison.Ordinal);
            Assert.DoesNotContain(Key, content, StringComparison.Ordinal);
        }
    }

    private static async Task<string> ReadSharedAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
