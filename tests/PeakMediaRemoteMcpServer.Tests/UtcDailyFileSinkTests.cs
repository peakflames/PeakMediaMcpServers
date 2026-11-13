namespace PeakMediaRemoteMcpServer.Tests;

public class UtcDailyFileSinkTests
{
    [Fact]
    public void File_name_uses_utc_date_not_local_date()
    {
        // 2026-11-12 23:30 at UTC-05:00 is 2026-11-13 04:30 UTC.
        var local = new DateTimeOffset(2026, 11, 12, 23, 30, 0, TimeSpan.FromHours(-5));

        Assert.Equal("Gateway_20261113.log", PeakMediaMcpTools.UtcDailyFileSink.FileNameFor("Gateway", local));
    }

    [Fact]
    public void Sink_switches_files_when_the_utc_date_changes()
    {
        using var directory = new TempDirectory();
        var now = new DateTimeOffset(2026, 11, 12, 23, 59, 0, TimeSpan.Zero);
        var sink = new PeakMediaMcpTools.UtcDailyFileSink(directory.Path, "Gw", PeakMediaMcpTools.LoggingSetup.OutputTemplate, () => now);
        using (var logger = new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger())
        {
            logger.Information("before midnight");
            now = now.AddMinutes(2);
            logger.Information("after midnight");
        }

        Assert.Contains("before midnight", File.ReadAllText(directory.File("Gw_20261112.log")));
        Assert.Contains("after midnight", File.ReadAllText(directory.File("Gw_20261113.log")));
    }
}
