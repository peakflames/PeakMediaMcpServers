namespace PeakMediaMcpTools.Narration;

/// <summary>
/// Whose OpenRouter account and audio cache a narration call uses, as the caller is told when one of them
/// fails: the gateway's (shared key, ask an administrator) or the solo user's own (their key, their local
/// cache directory). Everything else about a call is the same on both servers.
/// </summary>
public sealed record NarrationAccount(string KeyRejected, string OutOfCredit, string StorageFailure)
{
    /// <summary>The gateway: the shared key and storage belong to an administrator.</summary>
    public static readonly NarrationAccount Gateway = new(
        ToolErrorFormatter.Format("the gateway's OpenRouter key was rejected", "Ask an administrator to check the gateway's key."),
        ToolErrorFormatter.Format("the gateway's OpenRouter account is out of credit", "Ask an administrator to add credit."),
        ToolErrorFormatter.Format(
            "the gateway's audio cache is unavailable",
            "Try again in a moment; if it keeps failing, ask an administrator to check the gateway's storage."));

    /// <summary>The stdio server: the user's own key and local cache directory.</summary>
    public static readonly NarrationAccount Personal = new(
        ToolErrorFormatter.Format(
            "your OpenRouter key was rejected",
            "Check PEAK_MEDIA_OPENROUTER_API_KEY, export a valid key and restart the server."),
        ToolErrorFormatter.Format("your OpenRouter account is out of credit", "Add credit at openrouter.ai and run the same request again."),
        ToolErrorFormatter.Format(
            "the local audio cache directory is not writable",
            "Make PEAK_MEDIA_TTS_CACHE (or ~/.cache/peak-media/tts) writable and run the same request again."));
}
