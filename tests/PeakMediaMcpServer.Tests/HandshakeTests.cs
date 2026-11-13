namespace PeakMediaMcpServer.Tests;

[Collection("StdioProcess")]
public class HandshakeTests
{
    // TOR-01-p3K2fH3: the MCP initialize handshake reports the same name and version --version prints.
    [Fact]
    public async Task Initialize_reports_same_version_as_version_flag()
    {
        var flagVersion = await StdioServer.VersionFromFlagAsync();

        using var process = StdioServer.Start([], withKey: true);
        await process.WriteLineAsync(StdioServer.InitializeRequest);
        var response = await process.WaitForStdoutAsync(line => StdioServer.IsResponse(line, 1), StdioServer.Timeout);

        using var document = JsonDocument.Parse(response);
        var serverInfo = document.RootElement.GetProperty("result").GetProperty("serverInfo");
        Assert.Equal("PeakMediaMcpServer", serverInfo.GetProperty("name").GetString());
        Assert.Equal(flagVersion, serverInfo.GetProperty("version").GetString());
    }
}
