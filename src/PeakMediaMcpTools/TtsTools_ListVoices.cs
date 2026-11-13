namespace PeakMediaMcpTools;

public sealed partial class TtsTools
{
    [McpServerTool(Name = "list_voices", ReadOnly = true, Idempotent = true),
     Description("Lists the text-to-speech models this gateway allows, each with its voices, its default voice and whether a style is sent to it, and names the default model.")]
    public string ListVoices()
    {
        var text = new StringBuilder();
        text.AppendLine("## Voices");
        text.AppendLine();
        text.AppendLine($"Default model: `{catalog.DefaultModel}`");
        text.AppendLine();
        text.AppendLine("| model | default voice | style sent |");
        text.AppendLine("|---|---|---|");
        foreach (var model in catalog.Models)
        {
            text.AppendLine($"| {model.Id} | {model.DefaultVoice} | {(model.SendsStyle ? "yes" : "no")} |");
        }

        foreach (var model in catalog.Models)
        {
            text.AppendLine();
            text.AppendLine($"### {model.Id}");
            text.AppendLine();
            text.AppendLine($"Voices: {string.Join(", ", model.Voices)}");
        }

        return text.ToString();
    }
}
