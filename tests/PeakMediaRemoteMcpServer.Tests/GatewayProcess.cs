namespace PeakMediaRemoteMcpServer.Tests;

/// <summary>Helpers for launching the built gateway as a real child process on a free port.</summary>
internal static class GatewayProcess
{
    public const string Project = "PeakMediaRemoteMcpServer";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>The environment of a correctly configured gateway; callers override or remove (null) entries.</summary>
    public static Dictionary<string, string?> ValidEnvironment(int port, TempDirectory data)
    {
        return new Dictionary<string, string?>
        {
            ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}",
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["McpAuth__Enabled"] = "true",
            ["Broker__UpstreamAuthority"] = "https://example.okta.com",
            ["Broker__UpstreamClientId"] = "test-client",
            ["Broker__UpstreamClientSecret"] = "test-secret",
            ["OpenRouter__ApiKey"] = "test-key",
            ["Storage__DatabasePath"] = data.File("peakmedia.db"),
        };
    }

    public static ServerProcess Start(IDictionary<string, string?> environment)
    {
        return ServerProcess.Start(Project, [], environment);
    }

    /// <summary>Polls GET /version until the gateway answers.</summary>
    public static async Task WaitUntilReadyAsync(ServerProcess process, HttpClient client)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException($"gateway exited with {process.ExitCode}. stderr:\n{process.Stderr}");
            }

            try
            {
                using var response = await client.GetAsync("/version");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"gateway did not answer /version within {Timeout}. stderr:\n{process.Stderr}");
    }

    public static HttpClient ClientFor(int port)
    {
        return new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
    }
}
