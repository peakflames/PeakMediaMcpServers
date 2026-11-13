namespace PeakMediaMcpTools;

/// <summary>
/// The narration tools both servers offer — <c>synthesize_narration</c> and <c>list_voices</c> — one tool
/// per partial-class file, so their schemas are identical on the gateway and the stdio server by
/// construction. Not marked as a tool type on purpose: each host registers them explicitly
/// (<c>WithTools&lt;TtsTools&gt;()</c>) with its own audio cache (<see cref="IClipStore"/>) and usage log.
/// </summary>
public sealed partial class TtsTools(TtsCatalog catalog, NarrationService narration)
{
}
