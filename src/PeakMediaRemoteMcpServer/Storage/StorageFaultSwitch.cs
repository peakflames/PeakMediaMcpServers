namespace PeakMediaRemoteMcpServer.Storage;

/// <summary>
/// Test-only switch that makes storage fail or slow down, so error and loading states can be
/// exercised against the real running gateway. Read once from <c>PEAKMEDIA_TEST_STORAGE_FAULT</c>
/// (<c>fail</c> or <c>delay:&lt;ms&gt;</c>); ignored, with a warning, in the Production environment.
/// </summary>
public sealed class StorageFaultSwitch
{
    public const string VariableName = "PEAKMEDIA_TEST_STORAGE_FAULT";

    public static StorageFaultSwitch None { get; } = new(false, TimeSpan.Zero);

    public StorageFaultSwitch(bool fail, TimeSpan delay)
    {
        Fail = fail;
        Delay = delay;
    }

    public bool Fail { get; }

    public TimeSpan Delay { get; }

    public bool IsActive => Fail || Delay > TimeSpan.Zero;

    public static StorageFaultSwitch FromEnvironment(string? value, bool isProduction, Microsoft.Extensions.Logging.ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return None;
        }

        if (isProduction)
        {
            logger.LogWarning("{Variable} is set but ignored in Production. Unset it on production hosts.", VariableName);
            return None;
        }

        if (string.Equals(value, "fail", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("{Variable}=fail: every storage operation will fail (test switch).", VariableName);
            return new StorageFaultSwitch(true, TimeSpan.Zero);
        }

        const string delayPrefix = "delay:";
        if (value.StartsWith(delayPrefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value[delayPrefix.Length..], out var milliseconds)
            && milliseconds > 0)
        {
            logger.LogWarning("{Variable}: every storage operation is delayed by {Milliseconds} ms (test switch).", VariableName, milliseconds);
            return new StorageFaultSwitch(false, TimeSpan.FromMilliseconds(milliseconds));
        }

        logger.LogWarning("{Variable} has an unrecognized value and is ignored. Use 'fail' or 'delay:<ms>'.", VariableName);
        return None;
    }
}
