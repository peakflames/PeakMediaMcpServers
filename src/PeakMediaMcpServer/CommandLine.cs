namespace PeakMediaMcpServer;

/// <summary>What the stdio server was asked to do.</summary>
internal enum InvocationKind
{
    Serve,
    Version,
}

/// <summary>Parses the stdio server's command line. Anything but no args or <c>--version</c> is invalid.</summary>
internal static class CommandLine
{
    public static Result<InvocationKind> Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return Result.Ok(InvocationKind.Serve);
        }

        if (args.Count == 1 && args[0] == "--version")
        {
            return Result.Ok(InvocationKind.Version);
        }

        var offending = args[0] == "--version" ? args[1] : args[0];
        return Result.Fail<InvocationKind>(
            $"Error: unknown option '{offending}'. Run PeakMediaMcpServer --version, or start it with no arguments to serve MCP over stdio.");
    }
}
