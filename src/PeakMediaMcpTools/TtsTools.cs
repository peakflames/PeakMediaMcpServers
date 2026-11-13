namespace PeakMediaMcpTools;

/// <summary>
/// The text-to-speech tools, one tool per partial-class file. Not marked as a tool type on purpose:
/// they need the host's audio cache, so only the gateway registers them (<c>WithTools&lt;TtsTools&gt;()</c>);
/// the stdio server's assembly scan does not pick them up.
/// </summary>
public sealed partial class TtsTools(TtsCatalog catalog, NarrationService narration)
{
}
