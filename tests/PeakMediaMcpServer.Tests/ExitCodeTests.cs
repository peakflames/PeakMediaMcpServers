namespace PeakMediaMcpServer.Tests;

[Collection("StdioProcess")]
public class ExitCodeTests
{
    // CLAUDE.md exit-code convention: a missing OpenRouter key is an operational failure (exit 1)
    // whose stderr message names the next action.
    [Fact]
    public async Task Missing_key_exits_1_naming_next_action()
    {
        using var process = StdioServer.Start([], withKey: false);

        var exitCode = await process.WaitForExitAsync(StdioServer.Timeout);

        Assert.Equal(1, exitCode);
        Assert.Contains("Error: PEAK_MEDIA_OPENROUTER_API_KEY is not set. Export your OpenRouter key and restart.", process.Stderr);
        Assert.Empty(process.StdoutLines);
    }

    [Fact]
    public void Command_line_parses_no_args_as_serve_and_version_flag_as_version()
    {
        Assert.Equal(InvocationKind.Serve, CommandLine.Parse([]).Value);
        Assert.Equal(InvocationKind.Version, CommandLine.Parse(["--version"]).Value);
        Assert.True(CommandLine.Parse(["--version", "--extra"]).IsFailed);
    }
}
