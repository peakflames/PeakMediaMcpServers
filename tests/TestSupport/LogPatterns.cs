namespace PeakMediaTestSupport;

/// <summary>The log-record patterns the requirements state (TOR-01-VoM1MJ7, TOR-01-6Smkepz, startup records).</summary>
public static class LogPatterns
{
    public static readonly Regex Record = new(
        @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} \[(VRB|DBG|INF|WRN|ERR|FTL)\] .+");

    public static readonly Regex GatewayStartup = new(@"\[INF\] PeakMediaRemoteMcpServer v\d+\.\d+\.\d+ starting$");

    public static readonly Regex StdioStartup = new(@"\[INF\] PeakMediaMcpServer v\d+\.\d+\.\d+ starting$");
}
