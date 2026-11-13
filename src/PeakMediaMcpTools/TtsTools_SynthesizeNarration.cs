namespace PeakMediaMcpTools;

public sealed partial class TtsTools
{
    [McpServerTool(Name = "synthesize_narration"),
     Description("Turns a script of 1 to 40 clips (1 to 4,000 characters each) into speech through OpenRouter. Cached clips are free; on the gateway, a caller whose monthly quota is reached is refused. Returns per-clip metadata in one JSON block with, for each 16-bit mono 24 kHz WAV file, a short-lived download url (gateway) or a local file path (stdio server); sends one progress notification per clip.")]
    public async Task<string> SynthesizeNarration(
        [Description("The clips in order. Each has text, and optionally n (defaults to its position), style and voice overriding the call's.")]
        NarrationClipInput[]? clips,
        IProgress<ProgressNotificationValue> progress,
        [Description("Model id from list_voices; defaults to the server's default model.")]
        string? model = null,
        [Description("Voice name from list_voices for the model (case-insensitive); defaults to the model's default voice.")]
        string? voice = null,
        [Description("Delivery style sent as instructions to models that take one, e.g. \"Plain, measured delivery.\"")]
        string? style = null,
        CancellationToken cancellationToken = default)
    {
        var request = NarrationRequestValidator.Validate(catalog, clips, model, voice, style);
        if (request.IsFailed)
        {
            return request.Errors[0].Message;
        }

        var outcome = await narration.RunAsync(request.Value, progress, cancellationToken);
        return outcome.IsFailed
            ? outcome.Errors[0].Message
            : NarrationResultFormatter.Format(outcome.Value.Clips, outcome.Value.LocationKey, outcome.Value.QuotaNote);
    }
}
