namespace PeakMediaMcpTools.Narration;

/// <summary>The <c>Tts</c> section: the model allow-list, each model's voices, and the default model.</summary>
public sealed class TtsOptions
{
    public const string SectionName = "Tts";

    public string DefaultModel { get; set; } = string.Empty;

    public List<TtsModelOptions> Models { get; set; } = [];
}

/// <summary>One allowed model. <see cref="Family"/> names its voices in error text ("Gemini", "MAI").</summary>
public sealed class TtsModelOptions
{
    public string Id { get; set; } = string.Empty;

    public string Family { get; set; } = string.Empty;

    public string DefaultVoice { get; set; } = string.Empty;

    /// <summary>True when the style is sent upstream as <c>instructions</c> (Gemini); false for MAI.</summary>
    public bool SendsStyle { get; set; }

    public List<string> Voices { get; set; } = [];
}

/// <summary>Lookup over the configured allow-list. Model ids are exact; voice names match case-insensitively.</summary>
public sealed class TtsCatalog
{
    public TtsCatalog(TtsOptions options)
    {
        Options = options;
    }

    public TtsOptions Options { get; }

    public IReadOnlyList<TtsModelOptions> Models => Options.Models;

    public string DefaultModel => Options.DefaultModel;

    public TtsModelOptions? FindModel(string modelId)
    {
        return Options.Models.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.Ordinal));
    }

    /// <summary>The catalogue spelling of <paramref name="voice"/> for the model, or null when it has no such voice.</summary>
    public static string? FindVoice(TtsModelOptions model, string voice)
    {
        return model.Voices.FirstOrDefault(v => string.Equals(v, voice.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
