namespace PeakMediaRemoteMcpServer.Tests.Narration;

/// <summary>
/// A fact that calls the real OpenRouter and spends real credit. It runs only when
/// <see cref="KeyVariable"/> holds a key; otherwise the runner reports it as Skipped (never passed).
/// Run it through <c>build.py test-live</c>, which fails when it is skipped.
/// </summary>
public sealed class LiveOpenRouterFactAttribute : FactAttribute
{
    public const string KeyVariable = "PEAK_MEDIA_LIVE_OPENROUTER_API_KEY";

    public LiveOpenRouterFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            Skip = $"{KeyVariable} is not set — live OpenRouter test skipped";
        }
    }

    public static string Key => Environment.GetEnvironmentVariable(KeyVariable) ?? string.Empty;
}
