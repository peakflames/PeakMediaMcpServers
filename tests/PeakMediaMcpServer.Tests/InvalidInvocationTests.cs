namespace PeakMediaMcpServer.Tests;

[Collection("StdioProcess")]
public class InvalidInvocationTests
{
    // TOR-01-spPaZpz: an unknown flag exits 2, names the next action on stderr, and leaves stdout empty.
    [Fact]
    public async Task Unknown_flag_exits_2_with_next_action()
    {
        using var process = StdioServer.Start(["--bogus-flag"], withKey: true);
        process.CloseStdin();

        var exitCode = await process.WaitForExitAsync(StdioServer.Timeout);

        Assert.Equal(2, exitCode);
        Assert.Contains(
            "Error: unknown option '--bogus-flag'. Run PeakMediaMcpServer --version, or start it with no arguments to serve MCP over stdio.",
            process.Stderr);
        Assert.Empty(process.StdoutLines);
    }
}
