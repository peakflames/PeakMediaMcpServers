namespace PeakMediaMcpServer.Tests;

[Collection("StdioProcess")]
public class StdoutDisciplineTests
{
    // TOR-01-6Q14afY: after the initialize handshake and tools/list, every stdout line is a JSON-RPC 2.0
    // message, and the startup record is on stderr.
    [Fact]
    public async Task Stdout_carries_only_json_rpc()
    {
        using var process = StdioServer.Start([], withKey: true);
        await process.WriteLineAsync(StdioServer.InitializeRequest);
        await process.WaitForStdoutAsync(line => StdioServer.IsResponse(line, 1), StdioServer.Timeout);
        await process.WriteLineAsync(StdioServer.InitializedNotification);
        await process.WriteLineAsync(StdioServer.ToolsListRequest);
        await process.WaitForStdoutAsync(line => StdioServer.IsResponse(line, 2), StdioServer.Timeout);
        process.CloseStdin();
        await process.WaitForExitAsync(StdioServer.Timeout);

        Assert.NotEmpty(process.StdoutLines);
        Assert.All(process.StdoutLines, line =>
        {
            using var document = JsonDocument.Parse(line);
            Assert.Equal("2.0", document.RootElement.GetProperty("jsonrpc").GetString());
        });
        Assert.Contains(process.StderrLines, line => LogPatterns.StdioStartup.IsMatch(line));
    }
}
