namespace PeakMediaRemoteMcpServer.Tests.Narration;

[Collection("StdioProcess")]
public class StdioContractTests
{
    // TOR-06-PY6kH1r: synthesize_narration and list_voices have identical input schemas on the gateway
    // (signed-in caller) and the stdio server, and a stdio clip entry has the gateway's keys with url → path.
    [Fact]
    public async Task TOR_06_PY6kH1r_stdio_and_gateway_share_schemas_and_clip_fields()
    {
        await using var gateway = await NarrationClient.StartAsync();
        await using var fake = await FakeOpenRouter.StartListeningAsync();
        using var stdio = await StdioClient.StartAsync(fake.BaseUrl!);

        var gatewayTools = await gateway.ListToolsAsync();
        var stdioTools = await stdio.ListToolsAsync();
        foreach (var name in new[] { "synthesize_narration", "list_voices" })
        {
            var gatewaySchema = gatewayTools.Single(t => t.GetProperty("name").GetString() == name).GetProperty("inputSchema").GetRawText();
            var stdioSchema = stdioTools.Single(t => t.GetProperty("name").GetString() == name).GetProperty("inputSchema").GetRawText();
            Assert.Equal(gatewaySchema, stdioSchema);
        }

        var gatewayEntry = Assert.Single(NarrationClient.Entries((await gateway.SynthesizeAsync(NarrationClient.Clips(1))).Text));
        var stdioEntry = Assert.Single(StdioClient.Entries((await stdio.SynthesizeAsync(StdioClient.Clips(1))).Text));
        var gatewayKeys = gatewayEntry.EnumerateObject().Select(p => p.Name == "url" ? "path" : p.Name).ToList();
        var stdioKeys = stdioEntry.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("url", gatewayEntry.EnumerateObject().Select(p => p.Name));
        Assert.Equal(gatewayKeys, stdioKeys);
    }
}
