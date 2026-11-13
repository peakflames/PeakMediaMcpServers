namespace PeakMediaMcpServer.Tests;

[Collection("StdioProcess")]
public class StartupLogTests
{
    // TOR-01-7VjhchC: the first record on standard error is "[INF] PeakMediaMcpServer v<semver> starting".
    [Fact]
    public async Task First_stderr_record_is_startup_line()
    {
        using var process = StdioServer.Start([], withKey: true);
        await process.WaitForStderrAsync(LogPatterns.StdioStartup, StdioServer.Timeout);

        var first = process.StderrLines.First(line => line.Length > 0);
        Assert.Matches(LogPatterns.StdioStartup, first);
    }
}
