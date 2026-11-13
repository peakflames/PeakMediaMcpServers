namespace PeakMediaRemoteMcpServer.Tests;

[Collection("GatewayProcess")]
public class GatewayStartupRefusalTests
{
    // TOR-01-2lmaDWE: McpAuth:Enabled false → non-zero exit, the stated message on stderr, and GET /version
    // is never answered.
    [Fact]
    public async Task Refuses_when_McpAuth_disabled()
    {
        var process = await RunRefusedAsync(env => env["McpAuth__Enabled"] = "false", out var port);

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains(
            "McpAuth:Enabled is false. The gateway has no anonymous mode: set McpAuth:Enabled to true and configure the Broker settings, then restart.",
            process.Stderr);
        using var client = GatewayProcess.ClientFor(port);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("/version"));
        process.Dispose();
    }

    // TOR-01-EzMqmjt: McpAuth on, Broker__UpstreamClientSecret unset, OpenRouter key set → refuses.
    [Fact]
    public async Task Refuses_when_broker_secret_unset()
    {
        var process = await RunRefusedAsync(env => env["Broker__UpstreamClientSecret"] = null, out var port);

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains(
            "Broker__UpstreamClientSecret is not set. Set the OIDC client secret of the organization's sign-in app in the environment and restart.",
            process.Stderr);
        using var client = GatewayProcess.ClientFor(port);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("/version"));
        process.Dispose();
    }

    // TOR-01-a1AeUH3: McpAuth on with the Broker settings present, OpenRouter__ApiKey unset → refuses.
    [Fact]
    public async Task Refuses_when_openrouter_key_unset()
    {
        var process = await RunRefusedAsync(env => env["OpenRouter__ApiKey"] = null, out var port);

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains(
            "OpenRouter__ApiKey is not set. Set the organization's OpenRouter key in the environment and restart.",
            process.Stderr);
        using var client = GatewayProcess.ClientFor(port);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("/version"));
        process.Dispose();
    }

    private static Task<ServerProcess> RunRefusedAsync(Action<Dictionary<string, string?>> configure, out int port)
    {
        port = FreePort.Next();
        return RunAsync(configure, port);
    }

    private static async Task<ServerProcess> RunAsync(Action<Dictionary<string, string?>> configure, int port)
    {
        using var data = new TempDirectory();
        var environment = GatewayProcess.ValidEnvironment(port, data);
        configure(environment);
        var process = GatewayProcess.Start(environment);
        await process.WaitForExitAsync(GatewayProcess.Timeout);
        return process;
    }
}
