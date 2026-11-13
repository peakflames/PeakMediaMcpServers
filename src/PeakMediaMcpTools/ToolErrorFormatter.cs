namespace PeakMediaMcpTools;

/// <summary>
/// Builds the error text returned to MCP clients: <c>ERROR: &lt;problem&gt;. &lt;next action&gt;</c>.
/// Exception text is never echoed to the caller; every string passes through <see cref="Redact"/>.
/// </summary>
public static class ToolErrorFormatter
{
    private static readonly Regex AuthSchemeValue = new(
        @"(bearer|basic)\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Formats a tool error naming the problem and the next user action.</summary>
    public static string Format(string problem, string nextAction)
    {
        var trimmedProblem = Redact(problem).Trim().TrimEnd('.');
        var trimmedAction = Redact(nextAction).Trim();
        return $"ERROR: {trimmedProblem}. {trimmedAction}";
    }

    /// <summary>Replaces bearer/basic credential values with <c>[REDACTED]</c>.</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        return AuthSchemeValue.Replace(text, "$1 [REDACTED]");
    }
}
