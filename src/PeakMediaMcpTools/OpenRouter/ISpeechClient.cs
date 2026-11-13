namespace PeakMediaMcpTools.OpenRouter;

/// <summary>
/// Turns one clip into raw PCM (16-bit little-endian, mono, 24 kHz) and reads back what it cost.
/// <see cref="OpenRouterSpeechClient"/> calls OpenRouter; the stdio server also routes the offline
/// model <c>fake/tone</c> to <see cref="ToneSpeechClient"/>.
/// </summary>
public interface ISpeechClient
{
    /// <summary>Synthesizes one clip. A failure is a <see cref="SpeechError"/>.</summary>
    Task<Result<SpeechAudio>> SynthesizeAsync(
        TtsModelOptions model,
        string voice,
        string style,
        string text,
        CancellationToken cancellationToken);

    /// <summary>What the generation cost, or null when it cannot be read (the caller then estimates it).</summary>
    Task<decimal?> LookUpCostAsync(string? generationId, CancellationToken cancellationToken);
}
