namespace PeakMediaRemoteMcpServer.Tests;

public class ToolErrorFormatterTests
{
    [Fact]
    public void Format_names_problem_and_next_action()
    {
        var text = PeakMediaMcpTools.ToolErrorFormatter.Format(
            "daily quota of 50,000 characters reached",
            "Try again after 00:00 UTC or ask an administrator to raise your quota.");

        Assert.Equal(
            "ERROR: daily quota of 50,000 characters reached. Try again after 00:00 UTC or ask an administrator to raise your quota.",
            text);
    }

    [Fact]
    public void Format_redacts_bearer_values()
    {
        var text = PeakMediaMcpTools.ToolErrorFormatter.Format("upstream refused Bearer abc.def.ghi", "Retry.");

        Assert.DoesNotContain("abc.def.ghi", text, StringComparison.Ordinal);
        Assert.Contains("Bearer [REDACTED]", text, StringComparison.Ordinal);
    }
}
