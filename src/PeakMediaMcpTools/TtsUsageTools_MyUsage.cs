namespace PeakMediaMcpTools;

public sealed partial class TtsUsageTools
{
    [McpServerTool(Name = "my_usage", ReadOnly = true, Idempotent = true),
     Description("Shows your own usage for the current calendar month (UTC): month, amount spent, monthly quota, remaining amount, clips synthesized, cache hits and when the quota resets. US dollars to the cent.")]
    public async Task<string> MyUsage(CancellationToken cancellationToken = default)
    {
        var status = await usage.GetStatusAsync(cancellationToken);
        if (status.IsFailed)
        {
            return ToolErrorFormatter.Format(
                "your usage could not be read",
                "Try again in a moment; if it keeps failing, ask an administrator to check the gateway's storage.");
        }

        var value = status.Value;
        var text = new StringBuilder();
        text.AppendLine("## My usage");
        text.AppendLine();
        text.AppendLine("| field | value |");
        text.AppendLine("|---|---|");
        text.AppendLine($"| month | {value.Month} |");
        text.AppendLine($"| spent | {UsageText.Dollars(value.Spent)} |");
        text.AppendLine($"| quota | {UsageText.Dollars(value.Quota)} |");
        text.AppendLine($"| remaining | {UsageText.Dollars(value.Remaining)} |");
        text.AppendLine($"| clips synthesized | {value.ClipsSynthesized.ToString(CultureInfo.InvariantCulture)} |");
        text.AppendLine($"| cache hits | {value.CacheHits.ToString(CultureInfo.InvariantCulture)} |");
        text.Append($"| resets | {UsageText.Utc(value.ResetsUtc)} |");
        return text.ToString();
    }
}
