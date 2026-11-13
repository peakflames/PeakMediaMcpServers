namespace PeakMediaMcpServer;

/// <summary>
/// Routes each clip by model: <c>fake/tone</c> to the offline <see cref="ToneSpeechClient"/> (no network,
/// no cost), every other model to OpenRouter with the user's own key.
/// </summary>
internal sealed class SoloSpeechClient(OpenRouterSpeechClient openRouter, ToneSpeechClient tone) : ISpeechClient
{
    public Task<Result<SpeechAudio>> SynthesizeAsync(TtsModelOptions model, string voice, string style, string text, CancellationToken cancellationToken)
    {
        return IsTone(model.Id)
            ? tone.SynthesizeAsync(model, voice, style, text, cancellationToken)
            : openRouter.SynthesizeAsync(model, voice, style, text, cancellationToken);
    }

    public Task<decimal?> LookUpCostAsync(string? generationId, CancellationToken cancellationToken)
    {
        return string.Equals(generationId, ToneSpeechClient.GenerationId, StringComparison.Ordinal)
            ? tone.LookUpCostAsync(generationId, cancellationToken)
            : openRouter.LookUpCostAsync(generationId, cancellationToken);
    }

    private static bool IsTone(string modelId)
    {
        return string.Equals(modelId, ToneSpeechClient.ModelId, StringComparison.Ordinal);
    }
}
