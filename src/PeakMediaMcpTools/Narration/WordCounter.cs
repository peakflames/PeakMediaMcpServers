namespace PeakMediaMcpTools.Narration;

/// <summary>
/// Counts spoken words: mood tags such as <c>&lt;Warm&gt;</c> are not read aloud, so they are removed
/// before counting; a word is a whitespace-separated run holding at least one letter or digit.
/// </summary>
public static partial class WordCounter
{
    public static int Count(string text)
    {
        var withoutTags = MoodTag().Replace(text, " ");
        return withoutTags
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Count(token => token.Any(char.IsLetterOrDigit));
    }

    /// <summary>Words per minute, rounded; 0 when the clip has no duration.</summary>
    public static int WordsPerMinute(int words, int durationMs)
    {
        return durationMs <= 0 ? 0 : (int)Math.Round(words * 60000.0 / durationMs, MidpointRounding.AwayFromZero);
    }

    [GeneratedRegex(@"<[^<>]*>")]
    private static partial Regex MoodTag();
}
