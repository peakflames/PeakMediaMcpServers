namespace PeakMediaMcpTools.OpenRouter;

/// <summary>Raw PCM for one clip and the OpenRouter generation id its cost is looked up by.</summary>
public sealed record SpeechAudio(byte[] Pcm, string? GenerationId);

/// <summary>
/// How long to wait between generation-cost lookups. OpenRouter answers <c>GET generation</c> with 404
/// for several seconds after the speech call (seen: usually ready after 7 to 13 s, once after more than
/// 30 s), so the lookup is tried at once and again after each delay.
/// </summary>
public sealed class CostLookupSchedule(IReadOnlyList<TimeSpan> delays)
{
    /// <summary>0.5, 1, 2 and 4 s, then 8 s seven times — about 63.5 s before the cost is given up as 0.</summary>
    public static readonly CostLookupSchedule Default = FromMilliseconds([500, 1000, 2000, 4000, 8000, 8000, 8000, 8000, 8000, 8000, 8000]);

    public IReadOnlyList<TimeSpan> Delays { get; } = delays;

    /// <summary>The configured delays, or <see cref="Default"/> when none are configured.</summary>
    public static CostLookupSchedule FromMilliseconds(IReadOnlyList<int>? delaysMs)
    {
        return delaysMs is null || delaysMs.Count == 0
            ? Default
            : new CostLookupSchedule([.. delaysMs.Select(ms => TimeSpan.FromMilliseconds(Math.Max(0, ms)))]);
    }
}

/// <summary>
/// OpenRouter's speech endpoint. The host configures the <see cref="HttpClient"/>: base address
/// <c>…/api/v1/</c> and the server-held key as a Bearer token (the key never passes through here, and
/// is never logged). Each clip is one <c>POST audio/speech</c> asking for raw PCM with zero data
/// retention; the real cost comes later from <c>GET generation?id=</c> (<see cref="LookUpCostAsync"/>).
/// </summary>
public sealed class OpenRouterSpeechClient(HttpClient http, CostLookupSchedule schedule)
{
    public const string GenerationIdHeader = "X-Generation-Id";

    public async Task<Result<SpeechAudio>> SynthesizeAsync(
        TtsModelOptions model,
        string voice,
        string style,
        string text,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = model.Id,
            ["voice"] = voice,
            ["input"] = text,
            ["response_format"] = "pcm",
            ["provider"] = new JsonObject { ["zdr"] = true },
        };
        if (model.SendsStyle && !string.IsNullOrWhiteSpace(style))
        {
            body["instructions"] = style;
        }

        try
        {
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("audio/speech", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("OpenRouter speech request for {Model} answered {Status}", model.Id, (int)response.StatusCode);
                return Result.Fail<SpeechAudio>($"OpenRouter answered {(int)response.StatusCode}");
            }

            var pcm = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var generationId = response.Headers.TryGetValues(GenerationIdHeader, out var ids) ? ids.FirstOrDefault() : null;
            return Result.Ok(new SpeechAudio(pcm, generationId));
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Log.Warning("OpenRouter speech request for {Model} failed: {Kind}", model.Id, ex.GetType().Name);
            return Result.Fail<SpeechAudio>(new Error("OpenRouter could not be reached").CausedBy(ex));
        }
    }

    /// <summary>
    /// What OpenRouter charged for a generation. Tries at once, then again after each delay of the
    /// <see cref="CostLookupSchedule"/> while the generation is not ready yet (404, 429, 5xx, or no
    /// <c>total_cost</c>). Gives 0, with a warning, when it never appears.
    /// </summary>
    public async Task<decimal> LookUpCostAsync(string? generationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(generationId))
        {
            Log.Warning("OpenRouter returned no generation id; recording the clip's cost as 0");
            return 0m;
        }

        var outcome = "no answer";
        for (var attempt = 0; attempt <= schedule.Delays.Count; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(schedule.Delays[attempt - 1], cancellationToken);
            }

            var (cost, retry, seen) = await TryLookUpCostAsync(generationId, cancellationToken);
            if (cost is { } value)
            {
                return value;
            }

            outcome = seen;
            if (!retry)
            {
                break;
            }
        }

        Log.Warning("OpenRouter generation statistics were unavailable ({Outcome}); recording the clip's cost as 0", outcome);
        return 0m;
    }

    private async Task<(decimal? Cost, bool Retry, string Seen)> TryLookUpCostAsync(string generationId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync($"generation?id={Uri.EscapeDataString(generationId)}", cancellationToken);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (document.RootElement.TryGetProperty("data", out var data)
                    && data.TryGetProperty("total_cost", out var cost)
                    && cost.TryGetDecimal(out var value))
                {
                    return (value, false, status.ToString(CultureInfo.InvariantCulture));
                }

                return (null, true, $"{status.ToString(CultureInfo.InvariantCulture)} without total_cost");
            }

            var retry = response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests || status >= 500;
            return (null, retry, status.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return (null, true, ex.GetType().Name);
        }
    }
}
