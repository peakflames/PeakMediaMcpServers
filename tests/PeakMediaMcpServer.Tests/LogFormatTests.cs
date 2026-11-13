namespace PeakMediaMcpServer.Tests;

[Collection("StdioProcess")]
public class LogFormatTests
{
    // TOR-01-6Smkepz: every stderr record matches "<timestamp ms offset> [LVL] message" and a log file
    // exists under logs/ next to the stdio server binary.
    [Fact]
    public async Task Every_stderr_record_matches_format_and_log_file_exists()
    {
        using var process = StdioServer.Start([], withKey: true);
        await process.WriteLineAsync(StdioServer.InitializeRequest);
        await process.WaitForStdoutAsync(line => StdioServer.IsResponse(line, 1), StdioServer.Timeout);
        process.CloseStdin();
        await process.WaitForExitAsync(StdioServer.Timeout);

        var records = process.StderrLines.Where(line => line.Length > 0).ToList();
        Assert.NotEmpty(records);
        Assert.All(records, record => Assert.Matches(LogPatterns.Record, record));

        var logDirectory = RepoPaths.LogDirectory(StdioServer.Project);
        Assert.True(Directory.Exists(logDirectory), $"missing {logDirectory}");
        Assert.NotEmpty(Directory.GetFiles(logDirectory, "PeakMediaMcpServer_*.log"));
    }
}
