namespace PeakMediaMcpTools;

/// <summary>
/// The one Serilog configuration both servers use: plain text in the form
/// <c>yyyy-MM-dd HH:mm:ss.fff zzz [LVL] message</c>, written to standard error (never standard
/// output) and to a daily file <c>logs/&lt;Name&gt;_&lt;yyyyMMdd&gt;.log</c> next to the binary, the date
/// being the UTC date.
/// </summary>
public static class LoggingSetup
{
    public const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    public const string MinimumLevelKey = "Logging:MinimumLevel";

    /// <summary>The directory holding the daily log files: <c>logs/</c> next to the binary.</summary>
    public static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "logs");

    public static Logger CreateLogger(string serverName, IConfiguration? configuration)
    {
        var minimumLevel = ReadMinimumLevel(configuration);
        return new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Extensions.Hosting", LogEventLevel.Warning)
            .WriteTo.Console(outputTemplate: OutputTemplate, standardErrorFromLevel: LogEventLevel.Verbose)
            .WriteTo.Sink(new UtcDailyFileSink(LogDirectory, serverName, OutputTemplate))
            .CreateLogger();
    }

    private static LogEventLevel ReadMinimumLevel(IConfiguration? configuration)
    {
        var configured = configuration?[MinimumLevelKey];
        if (!string.IsNullOrWhiteSpace(configured) && Enum.TryParse<LogEventLevel>(configured, true, out var level))
        {
            return level;
        }

        return LogEventLevel.Information;
    }
}
