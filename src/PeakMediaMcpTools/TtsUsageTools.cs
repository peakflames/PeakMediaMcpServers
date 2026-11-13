namespace PeakMediaMcpTools;

/// <summary>
/// The gateway-only usage tool (<c>my_usage</c>), one tool per partial-class file. The stdio server has no
/// usage log or quota, so only the gateway registers it (<c>WithTools&lt;TtsUsageTools&gt;()</c>).
/// </summary>
public sealed partial class TtsUsageTools(IUsageLedger usage)
{
}
