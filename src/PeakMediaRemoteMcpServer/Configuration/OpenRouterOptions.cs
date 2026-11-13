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
}
