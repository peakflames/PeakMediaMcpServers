namespace PeakMediaMcpServer;

/// <summary>
/// The stdio server's <c>OpenRouter</c> section (environment <c>OpenRouter__*</c>): where to send speech
/// requests and how patiently. Deliberately holds no key — the user's key comes only from
/// <c>PEAK_MEDIA_OPENROUTER_API_KEY</c> (or the legacy <c>TTS_OPENROUTER_API_KEY</c>) and goes only on
/// the HTTP client's Authorization header.
/// </summary>
internal sealed class SoloOpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>Waits between generation-cost lookups; unset means the shared default schedule.</summary>
    public int[]? CostLookupDelaysMs { get; set; }

    /// <summary>How long one OpenRouter request may take before it counts as a timeout. Default 2 minutes.</summary>
    public TimeSpan ClipTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Wait, in milliseconds, before the one retry of a timed-out, unreachable or 5xx clip. Default 1000.</summary>
    public int RetryDelayMs { get; set; } = 1000;
}
