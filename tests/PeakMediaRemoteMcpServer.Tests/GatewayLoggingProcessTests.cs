namespace PeakMediaRemoteMcpServer.Tests;

[Collection("GatewayProcess")]
public class GatewayStartupLogTests
{
    // TOR-01-0wIkgxQ: the first stderr record is the startup line, and so is the first record of the
    // day's log file under logs/.
    [Fact]
    public async Task First_record_on_stderr_and_in_log_file_is_startup_line()
    {
        using var data = new TempDirectory();
        var port = FreePort.Next();
        using var process = GatewayProcess.Start(GatewayProcess.ValidEnvironment(port, data));
        using var client = GatewayProcess.ClientFor(port);
        await GatewayProcess.WaitUntilReadyAsync(process, client);

        var firstStderr = process.StderrLines.First(line => line.Length > 0);
        Assert.Matches(LogPatterns.GatewayStartup, firstStderr);

        var logFile = Path.Combine(
            RepoPaths.LogDirectory(GatewayProcess.Project),
            $"PeakMediaRemoteMcpServer_{DateTime.UtcNow:yyyyMMdd}.log");
        await process.StopAsync();
        var firstFileRecord = ReadLinesShared(logFile).First(line => line.Length > 0);
        Assert.Matches(LogPatterns.GatewayStartup, firstFileRecord);
    }

    internal static List<string> ReadLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }
}

[Collection("GatewayProcess")]
public class GatewayLogFormatTests
{
    // TOR-01-VoM1MJ7: after GET /version, every stderr record matches the plain-text format.
    [Fact]
    public async Task Every_stderr_record_matches_format()
    {
        using var data = new TempDirectory();
        var port = FreePort.Next();
        using var process = GatewayProcess.Start(GatewayProcess.ValidEnvironment(port, data));
        using var client = GatewayProcess.ClientFor(port);
        await GatewayProcess.WaitUntilReadyAsync(process, client);

        using var response = await client.GetAsync("/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await process.StopAsync();

        var records = process.StderrLines.Where(line => line.Length > 0).ToList();
        Assert.NotEmpty(records);
        Assert.All(records, record => Assert.Matches(LogPatterns.Record, record));
    }
}

[Collection("GatewayProcess")]
public class GatewayLogFileTests
{
    // TOR-01-mDo59qy: the daily file under logs/ next to the binary is named with the UTC date and holds
    // the startup record. Run in UTC+14 so the local date differs from the UTC date for most of the day.
    [Fact]
    public async Task Daily_file_named_with_utc_date_contains_startup_line()
    {
        using var data = new TempDirectory();
        var port = FreePort.Next();
        var environment = GatewayProcess.ValidEnvironment(port, data);
        environment["TZ"] = "Pacific/Kiritimati";
        var started = DateTime.UtcNow;

        using var process = GatewayProcess.Start(environment);
        using var client = GatewayProcess.ClientFor(port);
        await GatewayProcess.WaitUntilReadyAsync(process, client);
        using var response = await client.GetAsync("/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await process.StopAsync();

        // The startup line on stderr carries the +14:00 offset, proving the process ran in Kiritimati time.
        Assert.Contains("+14:00 [INF] PeakMediaRemoteMcpServer v", process.StderrLines.First(line => line.Length > 0));

        var utcDate = started.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var logDirectory = RepoPaths.LogDirectory(GatewayProcess.Project);
        var file = Directory.GetFiles(logDirectory).SingleOrDefault(f => Path.GetFileName(f).Contains(utcDate, StringComparison.Ordinal));
        Assert.NotNull(file);
        Assert.Contains(GatewayStartupLogTests.ReadLinesShared(file), line => LogPatterns.GatewayStartup.IsMatch(line) && line.Contains("+14:00", StringComparison.Ordinal));

        var kiritimatiDate = TimeZoneInfo.ConvertTimeFromUtc(started, TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati"))
            .ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        if (kiritimatiDate != utcDate)
        {
            var localNamed = Path.Combine(logDirectory, $"PeakMediaRemoteMcpServer_{kiritimatiDate}.log");
            Assert.False(
                File.Exists(localNamed) && File.GetLastWriteTimeUtc(localNamed) >= started,
                $"this run wrote a file named with the local (UTC+14) date: {localNamed}");
        }
    }
}

[Collection("GatewayProcess")]
public class GatewayStdoutTests
{
    // TOR-01-9QIaBsB: after GET /version and an unauthenticated GET /mcp, stdout is empty and stderr
    // holds the startup record.
    [Fact]
    public async Task Stdout_is_empty_after_version_and_unauthenticated_mcp()
    {
        using var data = new TempDirectory();
        var port = FreePort.Next();
        using var process = GatewayProcess.Start(GatewayProcess.ValidEnvironment(port, data));
        using var client = GatewayProcess.ClientFor(port);
        await GatewayProcess.WaitUntilReadyAsync(process, client);

        using var version = await client.GetAsync("/version");
        Assert.Equal(HttpStatusCode.OK, version.StatusCode);
        using var mcp = await client.GetAsync("/mcp");
        Assert.Equal(HttpStatusCode.Unauthorized, mcp.StatusCode);
        await process.StopAsync();

        Assert.Empty(process.StdoutLines);
        Assert.Contains(process.StderrLines, line => LogPatterns.GatewayStartup.IsMatch(line));
    }
}
