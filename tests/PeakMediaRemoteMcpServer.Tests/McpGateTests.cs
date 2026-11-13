namespace PeakMediaRemoteMcpServer.Tests;

public class McpGateTests
{
    private const string InitializeBody =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""";

    // /mcp is behind authorization: a request without a valid gateway access token is 401 (Epic Xq463wX;
    // the broker that issues tokens arrived in Epic 7BASFbp — see ResourceServerGateTests).
    [Fact]
    public async Task Mcp_without_token_is_401()
    {
        await using var host = await GatewayTestHost.StartAsync();

        using var response = await host.Client.PostAsync("/mcp", new StringContent(InitializeBody, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Mcp_with_unverifiable_bearer_is_401()
    {
        await using var host = await GatewayTestHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(InitializeBody, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            "eyJhbGciOiJub25lIn0.eyJzdWIiOiJ4In0.");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task Other_methods_without_token_are_401(string method)
    {
        await using var host = await GatewayTestHost.StartAsync();

        using var response = await host.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/mcp"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
