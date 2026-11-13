namespace PeakMediaMcpServer;

/// <summary>Reads the user's own OpenRouter key from the environment (never the shared gateway key).</summary>
internal static class OpenRouterKey
{
    public const string VariableName = "PEAK_MEDIA_OPENROUTER_API_KEY";
    public const string LegacyVariableName = "TTS_OPENROUTER_API_KEY";

    public const string MissingMessage =
        "Error: PEAK_MEDIA_OPENROUTER_API_KEY is not set. Export your OpenRouter key and restart.";

    public static Result<string> Read()
    {
        var key = Environment.GetEnvironmentVariable(VariableName);
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Environment.GetEnvironmentVariable(LegacyVariableName);
        }

        return string.IsNullOrWhiteSpace(key) ? Result.Fail<string>(MissingMessage) : Result.Ok(key);
    }
}
