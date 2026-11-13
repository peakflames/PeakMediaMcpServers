namespace PeakMediaRemoteMcpServer.Tests.Usage;

public class ToolListTests
{
    // TOR-02-dj60B0R: pat (not in the admin group) and ada (in the admin group) each get exactly
    // synthesize_narration, list_voices and my_usage, with identical input schemas.
    [Fact]
    public async Task TOR_02_dj60B0R_every_caller_gets_exactly_the_same_three_tools()
    {
        await using var client = await NarrationClient.StartAsync("pat@example.com");
        var ada = await client.SignInAnotherAsync(StubUser.For("ada@example.com", TestSignIn.AdminGroup));

        var patTools = await client.ListToolsAsync();
        var adaTools = await client.ListToolsAsync(ada);

        string[] expected = ["list_voices", "my_usage", "synthesize_narration"];
        Assert.Equal(expected, patTools.Select(t => t.GetProperty("name").GetString()).Order());
        Assert.Equal(expected, adaTools.Select(t => t.GetProperty("name").GetString()).Order());
        Assert.Equal(
            patTools.OrderBy(t => t.GetProperty("name").GetString()).Select(t => t.GetRawText()),
            adaTools.OrderBy(t => t.GetProperty("name").GetString()).Select(t => t.GetRawText()));
        Assert.All(patTools, t => Assert.True(t.TryGetProperty("inputSchema", out _)));
    }
}
