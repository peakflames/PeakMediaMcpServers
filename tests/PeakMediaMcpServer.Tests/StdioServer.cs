namespace PeakMediaMcpServer.Tests;

/// <summary>Helpers for launching the built stdio server as a child process.</summary>
internal static class StdioServer
{
    public const string Project = "PeakMediaMcpServer";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public const string InitializeRequest =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"peakmedia-tests","version":"1.0.0"}}}""";

    public const string InitializedNotification = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";

    public const string ToolsListRequest = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";

    public static ServerProcess Start(IEnumerable<string> args, bool withKey)
    {
        var environment = new Dictionary<string, string?>();
        if (withKey)
        {
            environment["PEAK_MEDIA_OPENROUTER_API_KEY"] = "sk-or-test-key";
        }

        return ServerProcess.Start(Project, args, environment);
    }

    /// <summary>Runs <c>--version</c> and returns the version it printed.</summary>
    public static async Task<string> VersionFromFlagAsync()
    {
        using var process = Start(["--version"], withKey: false);
        process.CloseStdin();
        await process.WaitForExitAsync(Timeout);
        var match = Regex.Match(process.StdoutLines.Single(), @"^PeakMediaMcpServer v(\d+\.\d+\.\d+)$");
        Assert.True(match.Success, $"unexpected --version output: {process.StdoutLines.Single()}");
        return match.Groups[1].Value;
    }

    public static bool IsResponse(string line, int id)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.TryGetProperty("id", out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.GetInt32() == id;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
