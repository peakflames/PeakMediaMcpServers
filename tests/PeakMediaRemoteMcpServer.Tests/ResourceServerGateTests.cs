namespace PeakMediaRemoteMcpServer.Tests;

public class ResourceServerGateTests
{
    // TOR-02-8R5ouns: an initialize to /mcp without Authorization → 401 whose WWW-Authenticate carries a
    // resource_metadata URL ending in /.well-known/oauth-protected-resource.
    [Fact]
    public async Task TOR_02_8R5ouns_unauthenticated_initialize_is_401_with_resource_metadata()
    {
        await using var host = await GatewayTestHost.StartAsync();

        using var response = await TestSignIn.InitializeAsync(host.Client, accessToken: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = string.Join(", ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));
        var match = Regex.Match(challenge, "resource_metadata=\"([^\"]+)\"");
        Assert.True(match.Success, $"no resource_metadata in WWW-Authenticate: {challenge}");
        Assert.EndsWith("/.well-known/oauth-protected-resource", match.Groups[1].Value, StringComparison.Ordinal);
    }

    // TOR-02-eXSTt0Q: GET /.well-known/oauth-protected-resource → 200; resource = G/mcp; authorization_servers ∋ G.
    [Fact]
    public async Task TOR_02_eXSTt0Q_protected_resource_metadata_names_gateway()
    {
        await using var host = await GatewayTestHost.StartAsync();

        using var response = await host.Client.GetAsync("/.well-known/oauth-protected-resource");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal($"{GatewayTestHost.BaseUrl}/mcp", document.RootElement.GetProperty("resource").GetString());
        var servers = document.RootElement.GetProperty("authorization_servers").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(GatewayTestHost.BaseUrl, servers);
    }

    public static TheoryData<string> RejectedTokens => new() { "expired", "malformed", "foreign" };

    // TOR-02-NwWTAqU: an expired gateway token, "not-a-token", and a well-formed token signed by another key
    // → each 401 with a WWW-Authenticate header.
    [Theory]
    [MemberData(nameof(RejectedTokens))]
    public async Task TOR_02_NwWTAqU_rejects_expired_malformed_and_foreign_tokens(string kind)
    {
        await using var host = await GatewayTestHost.StartAsync();
        var token = kind switch
        {
            "expired" => host.Services.GetRequiredService<JwtIssuer>()
                .IssueAccessToken("00u-expired", "pat@example.com", isAdmin: false, DateTimeOffset.UtcNow.AddHours(-2)),
            "malformed" => "not-a-token",
            _ => ForeignToken(),
        };

        using var response = await TestSignIn.InitializeAsync(host.Client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEmpty(response.Headers.WwwAuthenticate);
    }

    // TOR-02-GIUxnmc: /mcp?user=pat@example.com with X-User: pat@example.com and no Authorization → 401.
    [Fact]
    public async Task TOR_02_GIUxnmc_header_and_query_identity_is_ignored()
    {
        await using var host = await GatewayTestHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp?user=pat@example.com")
        {
            Content = new StringContent(TestSignIn.InitializeBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-User", "pat@example.com");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A well-formed RS256 token with the gateway's issuer and audience, signed by an unrelated key.</summary>
    private static string ForeignToken()
    {
        using var rsa = RSA.Create(2048);
        var now = DateTime.UtcNow;
        return new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().CreateToken(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Issuer = GatewayTestHost.BaseUrl,
            Audience = $"{GatewayTestHost.BaseUrl}/mcp",
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(30),
            Claims = new Dictionary<string, object> { ["sub"] = "00u-foreign", ["email"] = "pat@example.com", ["role"] = "admin" },
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.RsaSecurityKey(rsa.ExportParameters(true)) { KeyId = "foreign" },
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaSha256),
        });
    }
}
