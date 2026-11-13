namespace PeakMediaRemoteMcpServer.Configuration;

/// <summary>
/// The <c>OpenRouter</c> section. Holds the shared key, so it is a <c>sealed class</c>, never a record.
/// </summary>
public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    /// <summary>Arrives only through the <c>OpenRouter__ApiKey</c> environment variable.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>
    /// Waits, in milliseconds, between generation-cost lookups while OpenRouter has not published the
    /// clip's cost yet. Unset means the default
    /// 500, 1000, 2000, 4000, then 8000 seven times (about 63.5 s).
    /// </summary>
    public int[]? CostLookupDelaysMs { get; set; }

    /// <summary>
    /// How long one OpenRouter request (a clip, or a cost lookup) may take before it counts as a
    /// timeout. Default 2 minutes.
    /// </summary>
    public TimeSpan ClipTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Wait, in milliseconds, before the one retry of a timed-out, unreachable or 5xx clip. Default 1000.</summary>
    public int RetryDelayMs { get; set; } = 1000;
}
