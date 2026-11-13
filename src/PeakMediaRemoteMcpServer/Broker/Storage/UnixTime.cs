namespace PeakMediaRemoteMcpServer.Broker.Storage;

internal static class UnixTime
{
    public static long Of(DateTimeOffset value)
    {
        return value.ToUnixTimeSeconds();
    }

    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
