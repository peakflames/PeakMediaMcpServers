namespace PeakMediaMcpTools.OpenRouter;

/// <summary>
/// The offline model <c>fake/tone</c>: a quiet 440 Hz sine tone lasting as long as the clip's words take
/// at 150 words per minute (at least half a second). Free, makes no network call, needs no key — for
/// trying a pipeline end to end without spending credit.
/// </summary>
public sealed class ToneSpeechClient : ISpeechClient
{
    public const string ModelId = "fake/tone";

    /// <summary>The generation id every tone clip carries, so a router can send its cost lookup here.</summary>
    public const string GenerationId = "fake-tone";

    public const double WordsPerMinute = 150;

    private const double FrequencyHz = 440;
    private const double Amplitude = 0.2 * short.MaxValue;

    public Task<Result<SpeechAudio>> SynthesizeAsync(
        TtsModelOptions model,
        string voice,
        string style,
        string text,
        CancellationToken cancellationToken)
    {
        var words = Math.Max(1, WordCounter.Count(text));
        var durationMs = Math.Max(500, words * 60000.0 / WordsPerMinute);
        var samples = (int)Math.Round(durationMs * WavAudio.SampleRate / 1000.0);
        var pcm = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var value = (short)Math.Round(Amplitude * Math.Sin(2 * Math.PI * FrequencyHz * i / WavAudio.SampleRate));
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), value);
        }

        return Task.FromResult(Result.Ok(new SpeechAudio(pcm, GenerationId)));
    }

    /// <summary>A tone costs nothing.</summary>
    public Task<decimal?> LookUpCostAsync(string? generationId, CancellationToken cancellationToken)
    {
        return Task.FromResult<decimal?>(0m);
    }
}
