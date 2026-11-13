namespace PeakMediaMcpTools.OpenRouter;

/// <summary>Raw PCM for one clip and what OpenRouter charged for it.</summary>
public sealed record SpeechAudio(byte[] Pcm, decimal CostUsd);

/// <summary>
/// OpenRouter's speech endpoint. The host configures the <see cref="HttpClient"/>: base address
/// <c>…/api/v1/</c> and the server-held key as a Bearer token (the key never passes through here, and
/// is never logged). Each clip is one <c>POST audio/speech</c> asking for raw PCM with zero data
/// retention; the real cost comes from <c>GET generation?id=</c>.
/// </summary>
public sealed class OpenRouterSpeechClient(HttpClient http)
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
            var cost = await LookUpCostAsync(generationId, cancellationToken);
            return Result.Ok(new SpeechAudio(pcm, cost));
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Log.Warning("OpenRouter speech request for {Model} failed: {Kind}", model.Id, ex.GetType().Name);
            return Result.Fail<SpeechAudio>(new Error("OpenRouter could not be reached").CausedBy(ex));
        }
    }

    private async Task<decimal> LookUpCostAsync(string? generationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(generationId))
        {
            Log.Warning("OpenRouter returned no generation id; recording the clip's cost as 0");
            return 0m;
        }

        try
        {
            using var response = await http.GetAsync($"generation?id={Uri.EscapeDataString(generationId)}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (document.RootElement.TryGetProperty("data", out var data)
                    && data.TryGetProperty("total_cost", out var cost)
                    && cost.TryGetDecimal(out var value))
                {
                    return value;
                }
            }

            Log.Warning("OpenRouter generation statistics were unavailable ({Status}); recording the clip's cost as 0", (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Log.Warning("OpenRouter generation statistics could not be read ({Kind}); recording the clip's cost as 0", ex.GetType().Name);
        }

        return 0m;
    }
}
