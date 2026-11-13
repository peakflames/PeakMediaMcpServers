namespace PeakMediaMcpTools.Narration;

/// <summary>One clip as the caller sends it. <c>n</c> defaults to the clip's position (1-based).</summary>
public sealed record NarrationClipInput
{
    [JsonPropertyName("n")]
    public int? N { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("style")]
    public string? Style { get; init; }

    [JsonPropertyName("voice")]
    public string? Voice { get; init; }
}

/// <summary>A clip that passed validation: voice in catalogue casing, style resolved (clip style over call style).</summary>
public sealed record ValidClip(int N, string Voice, string Style, string Text);

public sealed record ValidNarrationRequest(TtsModelOptions Model, IReadOnlyList<ValidClip> Clips);

/// <summary>
/// Refuses every request that could not succeed before anything is spent. Each failure is the exact
/// <c>ERROR:</c> text returned to the caller, naming the problem and the fix.
/// </summary>
public static class NarrationRequestValidator
{
    public const int MaxClips = 40;
    public const int MaxCharacters = 4000;

    public static Result<ValidNarrationRequest> Validate(
        TtsCatalog catalog,
        IReadOnlyList<NarrationClipInput>? clips,
        string? model,
        string? voice,
        string? style)
    {
        if (clips is null || clips.Count == 0)
        {
            return Fail("no clips were given", "Send between 1 and 40 clips.");
        }

        if (clips.Count > MaxClips)
        {
            return Fail(
                $"{clips.Count.ToString(CultureInfo.InvariantCulture)} clips were given and the limit is {MaxClips} per call",
                $"Split the script into calls of at most {MaxClips} clips.");
        }

        var modelId = string.IsNullOrWhiteSpace(model) ? catalog.DefaultModel : model.Trim();
        var found = catalog.FindModel(modelId);
        if (found is null)
        {
            return Fail($"unknown model '{modelId}'", "Call list_voices and pick one of its models.");
        }

        var callVoice = found.DefaultVoice;
        if (!string.IsNullOrWhiteSpace(voice))
        {
            var normalized = TtsCatalog.FindVoice(found, voice);
            if (normalized is null)
            {
                return UnknownVoice(voice, found);
            }

            callVoice = normalized;
        }

        var valid = new List<ValidClip>(clips.Count);
        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            var n = clip.N ?? (i + 1);
            var label = n.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(clip.Text))
            {
                return Fail($"clip {label} has no text", "Give every clip between 1 and 4,000 characters of text.");
            }

            if (clip.Text.Length > MaxCharacters)
            {
                return Fail(
                    $"clip {label} has {clip.Text.Length.ToString("N0", CultureInfo.InvariantCulture)} characters and the limit is 4,000",
                    "Split it into shorter clips.");
            }

            var clipVoice = callVoice;
            if (!string.IsNullOrWhiteSpace(clip.Voice))
            {
                var normalized = TtsCatalog.FindVoice(found, clip.Voice);
                if (normalized is null)
                {
                    return UnknownVoice(clip.Voice, found);
                }

                clipVoice = normalized;
            }

            var clipStyle = !string.IsNullOrWhiteSpace(clip.Style) ? clip.Style.Trim() : (style?.Trim() ?? string.Empty);
            valid.Add(new ValidClip(n, clipVoice, clipStyle, clip.Text));
        }

        return Result.Ok(new ValidNarrationRequest(found, valid));
    }

    private static Result<ValidNarrationRequest> UnknownVoice(string voice, TtsModelOptions model)
    {
        return Fail($"unknown voice '{voice.Trim()}' for {model.Id}", $"Call list_voices and pick one of its {model.Family} voices.");
    }

    private static Result<ValidNarrationRequest> Fail(string problem, string nextAction)
    {
        return Result.Fail<ValidNarrationRequest>(ToolErrorFormatter.Format(problem, nextAction));
    }
}
