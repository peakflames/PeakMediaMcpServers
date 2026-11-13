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

    /// <summary>The clip-entry field for a gateway download link.</summary>
    public const string UrlKey = "url";

    /// <summary>The clip-entry field for a stdio server's local file path.</summary>
    public const string PathKey = "path";

    /// <summary>
    /// Formats the result. Each clip's <see cref="ClipResult.Location"/> is written under
    /// <paramref name="locationKey"/> (<c>url</c> or <c>path</c>), as the last field of the entry.
    /// </summary>
    public static string Format(IReadOnlyList<ClipResult> clips, string locationKey, string? quotaNote = null)
    {
        var median = MedianWpm(clips);
        var cached = clips.Count(c => c.Cached);
        var cost = clips.Sum(c => c.CostUsd);
        var text = new StringBuilder();
        text.AppendLine("## Narration");
        text.AppendLine();
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{clips.Count} clips · {cached} cached · {CallCost(cost)} this call · median {median} wpm"));

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
            text.AppendLine($"  {Entry(clips[i], locationKey).ToJsonString(EntryJson)}{separator}");
        }

        text.AppendLine("]");
        text.AppendLine("```");
        text.AppendLine();
        text.Append(locationKey == PathKey
            ? "Each `path` is the clip's WAV in your local cache; the same request again is served from it at no cost."
            : "Each `url` downloads the clip's WAV for 15 minutes; call synthesize_narration again with the same request for new links — cached clips are free.");
        if (!string.IsNullOrEmpty(quotaNote))
        {
            text.AppendLine();
            text.AppendLine();
            text.Append(quotaNote);
        }

        return text.ToString();
    }

    private static JsonObject Entry(ClipResult clip, string locationKey)
    {
        var entry = JsonSerializer.SerializeToNode(clip, EntryJson)!.AsObject();
        entry.Remove("location");
        entry[locationKey] = clip.Location;
        return entry;
    }

    /// <summary>A call's cost: <c>$0.00</c> for a free call, otherwise four decimals (e.g. <c>$0.0100</c>).</summary>
    public static string CallCost(decimal cost)
    {
        return cost == 0m
            ? "$0.00"
            : string.Create(CultureInfo.InvariantCulture, $"${cost:0.0000}");
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
