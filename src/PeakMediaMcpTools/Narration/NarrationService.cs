namespace PeakMediaMcpTools.Narration;

/// <summary>One clip of a finished call, in the shape returned to the caller.</summary>
public sealed record ClipResult(
    int N,
    string CacheKey,
    int DurationMs,
    int Words,
    int Wpm,
    bool Cached,
    decimal CostUsd,
    int SampleRate,
    string Url);

/// <summary>
/// Runs a validated request clip by clip, in order: a cache hit costs nothing; a miss is synthesized,
/// wrapped as WAV and stored at once, so clips finished before a failure stay cached. One progress
/// report per clip.
/// </summary>
public sealed class NarrationService(IClipStore clips, OpenRouterSpeechClient openRouter)
{
    public async Task<Result<IReadOnlyList<ClipResult>>> RunAsync(
        ValidNarrationRequest request,
        IProgress<ProgressNotificationValue>? progress,
        CancellationToken cancellationToken)
    {
        var results = new List<ClipResult>(request.Clips.Count);
        var total = request.Clips.Count;
        for (var i = 0; i < total; i++)
        {
            var clip = request.Clips[i];
            var key = ClipCacheKey.For(request.Model.Id, clip.Voice, clip.Style, clip.Text);
            var position = $"clip {(i + 1).ToString(CultureInfo.InvariantCulture)}/{total.ToString(CultureInfo.InvariantCulture)}";

            var found = await clips.FindAsync(key, cancellationToken);
            if (found.IsFailed)
            {
                return StorageFailure();
            }

            int durationMs;
            decimal cost;
            bool cached;
            string message;
            if (found.Value is { } hit)
            {
                durationMs = hit.DurationMs;
                cost = 0m;
                cached = true;
                message = $"{position} cached";
            }
            else
            {
                var audio = await openRouter.SynthesizeAsync(request.Model, clip.Voice, clip.Style, clip.Text, cancellationToken);
                if (audio.IsFailed)
                {
                    return Result.Fail<IReadOnlyList<ClipResult>>(ToolErrorFormatter.Format(
                        $"OpenRouter could not synthesize clip {clip.N.ToString(CultureInfo.InvariantCulture)}",
                        "Try the same call again in a moment; clips already finished are cached and free."));
                }

                durationMs = WavAudio.DurationMs(audio.Value.Pcm.Length);
                var saved = await clips.SaveAsync(key, WavAudio.FromPcm(audio.Value.Pcm), durationMs, cancellationToken);
                if (saved.IsFailed)
                {
                    return StorageFailure();
                }

                cost = audio.Value.CostUsd;
                cached = false;
                message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{position} synthesized ({durationMs / 1000.0:0.0} s audio, ${cost:0.0000})");
            }

            var url = await clips.IssueDownloadUrlAsync(key, cancellationToken);
            if (url.IsFailed)
            {
                return StorageFailure();
            }

            var words = WordCounter.Count(clip.Text);
            results.Add(new ClipResult(
                clip.N,
                key,
                durationMs,
                words,
                WordCounter.WordsPerMinute(words, durationMs),
                cached,
                cost,
                WavAudio.SampleRate,
                url.Value));
            progress?.Report(new ProgressNotificationValue { Progress = i + 1, Total = total, Message = message });
        }

        return Result.Ok<IReadOnlyList<ClipResult>>(results);
    }

    private static Result<IReadOnlyList<ClipResult>> StorageFailure()
    {
        return Result.Fail<IReadOnlyList<ClipResult>>(ToolErrorFormatter.Format(
            "the gateway's audio cache is unavailable",
            "Try again in a moment; if it keeps failing, ask an administrator to check the gateway's storage."));
    }
}
