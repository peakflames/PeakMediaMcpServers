namespace PeakMediaMcpTools.Narration;

/// <summary>
/// The markdown result of <c>synthesize_narration</c>: a summary line, one warning per slow clip, and
/// exactly one fenced JSON block holding the clip entries. Text only — never audio bytes.
/// </summary>
public static class NarrationResultFormatter
{
    /// <summary>A clip slower than this share of the call's median wpm is flagged.</summary>
    public const double SlowClipRatio = 0.7;

    private static readonly JsonSerializerOptions EntryJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Format(IReadOnlyList<ClipResult> clips)
    {
        var median = MedianWpm(clips);
        var cached = clips.Count(c => c.Cached);
        var cost = clips.Sum(c => c.CostUsd);
        var text = new StringBuilder();
        text.AppendLine("## Narration");
        text.AppendLine();
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{clips.Count} clips · {cached} cached · ${cost:0.0000} this call · median {median} wpm"));

        var slow = clips.Where(c => c.Wpm < median * SlowClipRatio).ToList();
        if (slow.Count > 0)
        {
            text.AppendLine();
            foreach (var clip in slow)
            {
                text.AppendLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"> ⚠ Warning: clip {clip.N} runs at {clip.Wpm} wpm, below 70% of the median {median} wpm — a mood tag may have been read aloud. Listen to it and re-synthesize it with corrected text if so."));
            }
        }

        text.AppendLine();
        text.AppendLine("```json");
        text.AppendLine("[");
        for (var i = 0; i < clips.Count; i++)
        {
            var separator = i < clips.Count - 1 ? "," : string.Empty;
            text.AppendLine($"  {JsonSerializer.Serialize(clips[i], EntryJson)}{separator}");
        }

        text.AppendLine("]");
        text.AppendLine("```");
        text.AppendLine();
        text.Append("Each `url` downloads the clip's WAV for 15 minutes; call synthesize_narration again with the same request for new links — cached clips are free.");
        return text.ToString();
    }

    /// <summary>The median of the clips' wpm, rounded to a whole number.</summary>
    public static int MedianWpm(IReadOnlyList<ClipResult> clips)
    {
        if (clips.Count == 0)
        {
            return 0;
        }

        var sorted = clips.Select(c => c.Wpm).Order().ToArray();
        var middle = sorted.Length / 2;
        var median = sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
        return (int)Math.Round(median, MidpointRounding.AwayFromZero);
    }
}
