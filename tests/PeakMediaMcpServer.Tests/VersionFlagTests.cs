namespace PeakMediaMcpServer.Tests;

[Collection("StdioProcess")]
public class VersionFlagTests
{
    // TOR-01-zQT2lRH: --version prints exactly one line "PeakMediaMcpServer v<semver>" and exits 0.
    [Fact]
    public async Task Version_flag_prints_name_and_semver_and_exits_0()
    {
        using var process = StdioServer.Start(["--version"], withKey: false);
        process.CloseStdin();

        var exitCode = await process.WaitForExitAsync(StdioServer.Timeout);

        Assert.Equal(0, exitCode);
        var line = Assert.Single(process.StdoutLines);
        Assert.Matches(new Regex(@"^PeakMediaMcpServer v\d+\.\d+\.\d+$"), line);
    }
}
