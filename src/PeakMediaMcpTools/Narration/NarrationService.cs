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

/// <summary>A finished call: its clips in order, and the over-quota note when the call ended at or over quota.</summary>
public sealed record NarrationRun(IReadOnlyList<ClipResult> Clips, string? QuotaNote);

/// <summary>
/// Runs a validated request clip by clip, in order. The caller's monthly quota is checked first: a caller
/// whose month-to-date spend has reached it is refused before any cache lookup or synthesis. Then a cache
/// hit costs nothing (logged as a free hit); a miss is synthesized, wrapped as WAV and stored at once, so
/// clips finished before a failure stay cached. OpenRouter publishes a clip's cost only seconds later, so
/// each clip is charged (<see cref="ChargeAsync"/>) in the background while the next clip synthesizes;
/// clips are reported (one progress notification each) and returned strictly in order, each once its cost
/// is known. A call that fails or is cancelled part-way still charges every clip already synthesized.
/// Narration text is never logged or stored — only its character count and cache key.
/// </summary>
public sealed class NarrationService(IClipStore clips, OpenRouterSpeechClient openRouter, IUsageLedger usage)
{
    public async Task<Result<NarrationRun>> RunAsync(
        ValidNarrationRequest request,
        IProgress<ProgressNotificationValue>? progress,
        CancellationToken cancellationToken)
    {
        var before = await usage.GetStatusAsync(cancellationToken);
        if (before.IsFailed)
        {
            return StorageFailure();
        }

        if (before.Value.IsOverQuota)
        {
            return Result.Fail<NarrationRun>(QuotaRefusal(before.Value));
        }

        var total = request.Clips.Count;
        var pending = new List<PendingClip>(total);
        var results = new List<ClipResult>(total);
        try
        {
            for (var i = 0; i < total; i++)
            {
                var clip = request.Clips[i];
                var key = ClipCacheKey.For(request.Model.Id, clip.Voice, clip.Style, clip.Text);

                var found = await clips.FindAsync(key, cancellationToken);
                if (found.IsFailed)
                {
                    return await FailAfterChargingAsync(pending, StorageFailureText);
                }

                if (found.Value is { } hit)
                {
                    await RecordAsync(new UsageEntry(request.Model.Id, clip.Voice, clip.Text.Length, 0m, key, CacheHit: true));
                    pending.Add(new PendingClip(clip, key, hit.DurationMs, true, Task.FromResult(0m)));
                }
                else
                {
                    var audio = await openRouter.SynthesizeAsync(request.Model, clip.Voice, clip.Style, clip.Text, cancellationToken);
                    if (audio.IsFailed)
                    {
                        // Stop at the failing clip: later clips are never requested. Clips before it are saved and charged.
                        return await FailAfterChargingAsync(pending, SpeechFailure(audio.Errors[0], clip.N, i + 1, total, savedBefore: i));
                    }

                    var durationMs = WavAudio.DurationMs(audio.Value.Pcm.Length);
                    var saved = await clips.SaveAsync(key, WavAudio.FromPcm(audio.Value.Pcm), durationMs, cancellationToken);
                    var cost = ChargeAsync(request.Model, clip, key, audio.Value.GenerationId, cancellationToken);
                    pending.Add(new PendingClip(clip, key, durationMs, false, cost));
                    if (saved.IsFailed)
                    {
                        return await FailAfterChargingAsync(pending, StorageFailureText);
                    }
                }

                if (!await ReportReadyAsync(pending, results, total, progress, waitForAll: false, cancellationToken))
                {
                    return await FailAfterChargingAsync(pending, StorageFailureText);
                }
            }

            if (!await ReportReadyAsync(pending, results, total, progress, waitForAll: true, cancellationToken))
            {
                return await FailAfterChargingAsync(pending, StorageFailureText);
            }
        }
        catch (OperationCanceledException)
        {
            // The caller gave up: every clip already synthesized was paid for, so it is still charged.
            await DrainAsync(pending);
            throw;
        }

        var after = await usage.GetStatusAsync(CancellationToken.None);
        string? note = null;
        if (after.IsFailed)
        {
            Log.Warning("Usage could not be read after a narration call; the over-quota note was skipped");
        }
        else if (after.Value.IsOverQuota)
        {
            note = $"You are now over your monthly quota ({UsageText.Dollars(after.Value.Spent)} of {UsageText.Dollars(after.Value.Quota)}).";
        }

        return Result.Ok(new NarrationRun(results, note));
    }

    /// <summary>
    /// The refusal for a caller whose month-to-date spend has reached their quota, naming the quota, the
    /// spend and the reset time.
    /// </summary>
    public static string QuotaRefusal(UsageStatus status)
    {
        return ToolErrorFormatter.Format(
            $"monthly quota of {UsageText.Dollars(status.Quota)} reached ({UsageText.Dollars(status.Spent)} spent this month)",
            $"Try again after {UsageText.Utc(status.ResetsUtc)} or ask an administrator to raise your quota.");
    }

    /// <summary>
    /// Charges one synthesized clip: the cost OpenRouter reports, or — when it cannot be read — the model's
    /// configured price for the clip's characters, with a warning. The usage row is written even when the
    /// call is cancelled meanwhile (the audio was already paid for); then the cancellation propagates.
    /// </summary>
    private async Task<decimal> ChargeAsync(TtsModelOptions model, ValidClip clip, string key, string? generationId, CancellationToken cancellationToken)
    {
        decimal? reported = null;
        try
        {
            reported = await openRouter.LookUpCostAsync(generationId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancelled while waiting for OpenRouter's statistics: charge the estimate below, then rethrow.
        }

        var cost = reported ?? EstimatedCost(model, clip.Text.Length);
        await RecordAsync(new UsageEntry(model.Id, clip.Voice, clip.Text.Length, cost, key, CacheHit: false));
        cancellationToken.ThrowIfCancellationRequested();
        return cost;
    }

    private static decimal EstimatedCost(TtsModelOptions model, int characters)
    {
        var estimate = model.EstimateCost(characters);
        Log.Warning(
            "OpenRouter's reported cost for a clip of {Model} could not be read; the cost was estimated from the configured price (${Estimate} for {Characters} characters)",
            model.Id,
            estimate.ToString("0.0000######", CultureInfo.InvariantCulture),
            characters);
        return estimate;
    }

    /// <summary>Writes a usage row. A failed write is logged and does not fail the call: the audio is already paid for.</summary>
    private async Task RecordAsync(UsageEntry entry)
    {
        var recorded = await usage.RecordAsync(entry, CancellationToken.None);
        if (recorded.IsFailed)
        {
            Log.Error("A usage record for {Model} could not be written; the clip was not charged to the caller", entry.Model);
        }
    }

    /// <summary>Waits for every pending clip's charge, then fails the call with <paramref name="message"/>.</summary>
    private static async Task<Result<NarrationRun>> FailAfterChargingAsync(List<PendingClip> pending, string message)
    {
        await DrainAsync(pending);
        return Result.Fail<NarrationRun>(message);
    }

    private static async Task DrainAsync(List<PendingClip> pending)
    {
        foreach (var clip in pending)
        {
            try
            {
                await clip.Cost;
            }
            catch (OperationCanceledException)
            {
                // Charged before the cancellation surfaced.
            }
        }
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

    private static readonly string StorageFailureText = ToolErrorFormatter.Format(
        "the gateway's audio cache is unavailable",
        "Try again in a moment; if it keeps failing, ask an administrator to check the gateway's storage.");

    private static Result<NarrationRun> StorageFailure()
    {
        return Result.Fail<NarrationRun>(StorageFailureText);
    }

    /// <summary>A clip that is synthesized or found in the cache, waiting for its cost before it is reported.</summary>
    private sealed record PendingClip(ValidClip Clip, string Key, int DurationMs, bool Cached, Task<decimal> Cost);
}
