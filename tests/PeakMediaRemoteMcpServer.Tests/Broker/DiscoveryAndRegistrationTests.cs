namespace PeakMediaRemoteMcpServer.Tests.Broker;

public class DiscoveryAndRegistrationTests
{
    // TOR-02-VOsRKOw: authorization-server metadata lists G/authorize, G/token, G/register and S256.
    [Fact]
    public async Task TOR_02_VOsRKOw_authorization_server_metadata_lists_endpoints_and_S256()
    {
        await using var host = await GatewayTestHost.StartAsync();

        using var response = await host.Client.GetAsync("/.well-known/oauth-authorization-server");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal($"{GatewayTestHost.BaseUrl}/authorize", root.GetProperty("authorization_endpoint").GetString());
        Assert.Equal($"{GatewayTestHost.BaseUrl}/token", root.GetProperty("token_endpoint").GetString());
        Assert.Equal($"{GatewayTestHost.BaseUrl}/register", root.GetProperty("registration_endpoint").GetString());
        Assert.Contains("S256", root.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(e => e.GetString()));
    }

    // TOR-02-Mb6PQo5: POST /register with a loopback redirect URI → 201, non-empty client_id, redirect_uris echoes it.
    [Fact]
    public async Task TOR_02_Mb6PQo5_register_loopback_client_returns_201_with_client_id()
    {
        await using var host = await GatewayTestHost.StartAsync();
        const string redirectUri = "http://localhost:51234/callback";

        using var response = await host.Client.PostAsync(
            "/register",
            new StringContent($$"""{"redirect_uris":["{{redirectUri}}"],"client_name":"Claude Code"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrEmpty(document.RootElement.GetProperty("client_id").GetString()));
        Assert.Contains(redirectUri, document.RootElement.GetProperty("redirect_uris").EnumerateArray().Select(e => e.GetString()));
        Assert.False(string.IsNullOrEmpty(document.RootElement.GetProperty("client_secret").GetString()));
    }

    [Theory]
    [InlineData("https://evil.example.com/callback")]
    [InlineData("http://localhost@evil.example.com/callback")]
    [InlineData("not a uri")]
    public async Task Register_rejects_non_loopback_redirect(string redirectUri)
    {
        await using var host = await GatewayTestHost.StartAsync();

        using var response = await host.Client.PostAsync(
            "/register",
            new StringContent(JsonSerializer.Serialize(new { redirect_uris = new[] { redirectUri } }), Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_redirect_uri", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Register_accepts_redirect_listed_in_AllowedRedirectUris()
    {
        await using var host = await GatewayTestHost.StartAsync(new Dictionary<string, string?>
        {
            ["Broker:AllowedRedirectUris:0"] = "https://app.example.com/oauth/done",
        });

        using var response = await host.Client.PostAsync(
            "/register",
            new StringContent("""{"redirect_uris":["https://app.example.com/oauth/done"]}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Jwks_publishes_the_public_signing_key()
    {
        await using var host = await GatewayTestHost.StartAsync();

        using var response = await host.Client.GetAsync("/.well-known/jwks.json");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var key = document.RootElement.GetProperty("keys")[0];
        Assert.Equal("RSA", key.GetProperty("kty").GetString());
        Assert.False(key.TryGetProperty("d", out _));
    }
}
