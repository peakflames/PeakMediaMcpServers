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
/// wrapped as WAV and stored at once, so clips finished before a failure stay cached. OpenRouter
/// publishes a clip's cost only seconds later, so each clip's cost lookup runs in the background while
/// the next clip synthesizes; clips are reported (one progress notification each) and returned strictly
/// in order, each once its cost is known.
/// </summary>
public sealed class NarrationService(IClipStore clips, OpenRouterSpeechClient openRouter)
{
    public async Task<Result<IReadOnlyList<ClipResult>>> RunAsync(
        ValidNarrationRequest request,
        IProgress<ProgressNotificationValue>? progress,
        CancellationToken cancellationToken)
    {
        var total = request.Clips.Count;
        var pending = new List<PendingClip>(total);
        var results = new List<ClipResult>(total);
        for (var i = 0; i < total; i++)
        {
            var clip = request.Clips[i];
            var key = ClipCacheKey.For(request.Model.Id, clip.Voice, clip.Style, clip.Text);

            var found = await clips.FindAsync(key, cancellationToken);
            if (found.IsFailed)
            {
                return StorageFailure();
            }

            if (found.Value is { } hit)
            {
                pending.Add(new PendingClip(clip, key, hit.DurationMs, true, Task.FromResult(0m)));
            }
            else
            {
                var audio = await openRouter.SynthesizeAsync(request.Model, clip.Voice, clip.Style, clip.Text, cancellationToken);
                if (audio.IsFailed)
                {
                    // Stop at the failing clip: later clips are never requested. Clips before it are saved.
                    return Result.Fail<IReadOnlyList<ClipResult>>(SpeechFailure(audio.Errors[0], clip.N, i + 1, total, savedBefore: i));
                }

                var durationMs = WavAudio.DurationMs(audio.Value.Pcm.Length);
                var saved = await clips.SaveAsync(key, WavAudio.FromPcm(audio.Value.Pcm), durationMs, cancellationToken);
                if (saved.IsFailed)
                {
                    return StorageFailure();
                }

                var cost = openRouter.LookUpCostAsync(audio.Value.GenerationId, cancellationToken);
                pending.Add(new PendingClip(clip, key, durationMs, false, cost));
            }

            if (!await ReportReadyAsync(pending, results, total, progress, waitForAll: false, cancellationToken))
            {
                return StorageFailure();
            }
        }

        if (!await ReportReadyAsync(pending, results, total, progress, waitForAll: true, cancellationToken))
        {
            return StorageFailure();
        }

        return Result.Ok<IReadOnlyList<ClipResult>>(results);
    }

    /// <summary>
    /// Reports, in clip order, every pending clip whose cost is known — or, with
    /// <paramref name="waitForAll"/>, every pending clip, waiting for each cost in turn.
    /// </summary>
    private async Task<bool> ReportReadyAsync(
        List<PendingClip> pending,
        List<ClipResult> results,
        int total,
        IProgress<ProgressNotificationValue>? progress,
        bool waitForAll,
        CancellationToken cancellationToken)
    {
        while (results.Count < pending.Count && (waitForAll || pending[results.Count].Cost.IsCompleted))
        {
            var index = results.Count;
            var next = pending[index];
            var cost = await next.Cost;
            var url = await clips.IssueDownloadUrlAsync(next.Key, cancellationToken);
            if (url.IsFailed)
            {
                return false;
            }

            var words = WordCounter.Count(next.Clip.Text);
            results.Add(new ClipResult(
                next.Clip.N,
                next.Key,
                next.DurationMs,
                words,
                WordCounter.WordsPerMinute(words, next.DurationMs),
                next.Cached,
                cost,
                WavAudio.SampleRate,
                url.Value));

            var position = $"clip {(index + 1).ToString(CultureInfo.InvariantCulture)}/{total.ToString(CultureInfo.InvariantCulture)}";
            var message = next.Cached
                ? $"{position} cached"
                : string.Create(CultureInfo.InvariantCulture, $"{position} synthesized ({next.DurationMs / 1000.0:0.0} s audio, ${cost:0.0000})");
            progress?.Report(new ProgressNotificationValue { Progress = index + 1, Total = total, Message = message });
        }

        return true;
    }

    /// <summary>
    /// The caller-facing text for a failed clip at <paramref name="position"/> of <paramref name="total"/>,
    /// with <paramref name="savedBefore"/> clips already in the cache. Never carries the key or exception text.
    /// </summary>
    public static string SpeechFailure(IError error, int clipN, int position, int total, int savedBefore)
    {
        var n = clipN.ToString(CultureInfo.InvariantCulture);
        if (error is not SpeechError speech)
        {
            return OtherFailure(n);
        }

        switch (speech.Kind)
        {
            case SpeechFailureKind.OutOfCredit:
                return ToolErrorFormatter.Format(
                    "the gateway's OpenRouter account is out of credit",
                    "Ask an administrator to add credit.");
            case SpeechFailureKind.KeyRejected:
                return ToolErrorFormatter.Format(
                    "the gateway's OpenRouter key was rejected",
                    "Ask an administrator to check the gateway's key.");
            case SpeechFailureKind.Timeout:
            case SpeechFailureKind.Unreachable:
            case SpeechFailureKind.ServerError:
                var reason = speech.Kind switch
                {
                    SpeechFailureKind.Timeout => "timeout",
                    SpeechFailureKind.Unreachable => "unreachable",
                    _ => $"status {speech.Status?.ToString(CultureInfo.InvariantCulture) ?? "5xx"}",
                };
                var retried = speech.Retried ? ", retried once" : string.Empty;
                return ToolErrorFormatter.Format(
                    $"OpenRouter did not answer for clip {n} of {total.ToString(CultureInfo.InvariantCulture)} ({reason}{retried})",
                    SavedClips(savedBefore));
            default:
                return OtherFailure(n);
        }
    }

    private static string SavedClips(int saved)
    {
        return saved switch
        {
            0 => "Run the same request again to retry.",
            1 => "Clip 1 is saved; run the same request again to continue.",
            _ => $"Clips 1–{saved.ToString(CultureInfo.InvariantCulture)} are saved; run the same request again to continue.",
        };
    }

    private static string OtherFailure(string n)
    {
        return ToolErrorFormatter.Format(
            $"OpenRouter could not synthesize clip {n}",
            "Try the same call again in a moment; clips already finished are cached and free.");
    }

    private static Result<IReadOnlyList<ClipResult>> StorageFailure()
    {
        return Result.Fail<IReadOnlyList<ClipResult>>(ToolErrorFormatter.Format(
            "the gateway's audio cache is unavailable",
            "Try again in a moment; if it keeps failing, ask an administrator to check the gateway's storage."));
    }

    /// <summary>A clip that is synthesized or found in the cache, waiting for its cost before it is reported.</summary>
    private sealed record PendingClip(ValidClip Clip, string Key, int DurationMs, bool Cached, Task<decimal> Cost);
}
