namespace PeakMediaMcpTools;

/// <summary>
/// A daily file sink whose file name carries the <b>UTC</b> date (<c>&lt;prefix&gt;_yyyyMMdd.log</c>).
/// Serilog.Sinks.File's own <c>rollingInterval: Day</c> rolls on the local clock, so a server in a
/// timezone ahead of or behind UTC would name its file with the wrong day. This sink keeps one
/// non-rolling file sink open per UTC day and swaps it when the UTC date changes.
/// </summary>
public sealed class UtcDailyFileSink : ILogEventSink, IDisposable
{
    private readonly string _directory;
    private readonly string _prefix;
    private readonly string _outputTemplate;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Lock _gate = new();
    private string? _currentDate;
    private Logger? _current;

    public UtcDailyFileSink(string directory, string prefix, string outputTemplate, Func<DateTimeOffset>? utcNow = null)
    {
        _directory = directory;
        _prefix = prefix;
        _outputTemplate = outputTemplate;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public static string FileNameFor(string prefix, DateTimeOffset utcNow)
    {
        return $"{prefix}_{utcNow.UtcDateTime:yyyyMMdd}.log";
    }

    public void Emit(LogEvent logEvent)
    {
        lock (_gate)
        {
            var now = _utcNow();
            var date = now.UtcDateTime.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            if (_current is null || _currentDate != date)
            {
                _current?.Dispose();
                Directory.CreateDirectory(_directory);
                _current = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .WriteTo.File(Path.Combine(_directory, FileNameFor(_prefix, now)), outputTemplate: _outputTemplate, shared: true)
                    .CreateLogger();
                _currentDate = date;
            }

            _current.Write(logEvent);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _current?.Dispose();
            _current = null;
        }
    }
}
