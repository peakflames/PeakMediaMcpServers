namespace PeakMediaRemoteMcpServer.Tests.Broker;

public class TokenEndpointTests
{
    // TOR-02-QFiwMY7: state "xyz", sign-in as pat@example.com → client redirect with code and state "xyz";
    // POST /token with the code and matching verifier → 200 with access_token and refresh_token; initialize
    // with the access token succeeds.
    [Fact]
    public async Task TOR_02_QFiwMY7_successful_sign_in_returns_code_and_tokens_that_open_mcp()
    {
        await using var stub = await StubOidcIssuer.StartAsync();
        await using var host = await GatewayTestHost.StartAsync(stub);
        stub.SetNextUser(StubUser.For("pat@example.com"));
        var client = await TestSignIn.RegisterAsync(host.Client);
        var verifier = TestSignIn.NewVerifier();
        var start = await TestSignIn.BeginAsync(host.Client, client, "xyz", TestSignIn.Challenge(verifier));
        var callback = await TestSignIn.FollowProviderAsync(start.Location!);

        var clientRedirect = await TestSignIn.CallbackAsync(host.Client, callback, start.ConsentCookie);

        Assert.StartsWith(client.RedirectUri, clientRedirect.ToString(), StringComparison.Ordinal);
        var query = TestSignIn.Query(clientRedirect);
        Assert.Equal("xyz", query["state"]);
        Assert.False(string.IsNullOrEmpty(query["code"]));

        using var tokenResponse = await TestSignIn.ExchangeCodeAsync(host.Client, client, query["code"]!, verifier);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var tokens = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var accessToken = tokens.RootElement.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrEmpty(accessToken));
        Assert.False(string.IsNullOrEmpty(tokens.RootElement.GetProperty("refresh_token").GetString()));

        using var initialize = await TestSignIn.InitializeAsync(host.Client, accessToken);
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        using var result = await TestSignIn.ReadJsonRpcAsync(initialize);
        Assert.True(result.RootElement.TryGetProperty("result", out _));
    }

    // TOR-02-x3ci8yI: a code issued for an S256 challenge, exchanged with a different verifier → 400,
    // error invalid_grant, no access_token.
    [Fact]
    public async Task TOR_02_x3ci8yI_wrong_code_verifier_is_invalid_grant()
    {
        await using var stub = await StubOidcIssuer.StartAsync();
        await using var host = await GatewayTestHost.StartAsync(stub);
        var client = await TestSignIn.RegisterAsync(host.Client);
        var start = await TestSignIn.BeginAsync(host.Client, client, "xyz", TestSignIn.Challenge(TestSignIn.NewVerifier()));
        var clientRedirect = await TestSignIn.CallbackAsync(host.Client, await TestSignIn.FollowProviderAsync(start.Location!), start.ConsentCookie);
        var code = TestSignIn.Query(clientRedirect)["code"]!;

        using var response = await TestSignIn.ExchangeCodeAsync(host.Client, client, code, TestSignIn.NewVerifier());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_grant", document.RootElement.GetProperty("error").GetString());
        Assert.False(document.RootElement.TryGetProperty("access_token", out _));
    }

    // TOR-02-kx8Nl0i: POST /token grant_type refresh_token → 200 with a new access_token that opens /mcp.
    [Fact]
    public async Task TOR_02_kx8Nl0i_refresh_token_issues_new_working_access_token()
    {
        await using var stub = await StubOidcIssuer.StartAsync();
        await using var host = await GatewayTestHost.StartAsync(stub);
        var signedIn = await TestSignIn.SignInAsync(host.Client, stub, StubUser.For("pat@example.com"));

        using var response = await TestSignIn.RefreshAsync(host.Client, signedIn.Client, signedIn.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var newAccessToken = document.RootElement.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrEmpty(newAccessToken));
        Assert.NotEqual(signedIn.AccessToken, newAccessToken);
        using var initialize = await TestSignIn.InitializeAsync(host.Client, newAccessToken);
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
    }

    [Fact]
    public async Task Authorization_code_is_single_use()
    {
        await using var stub = await StubOidcIssuer.StartAsync();
        await using var host = await GatewayTestHost.StartAsync(stub);
        var client = await TestSignIn.RegisterAsync(host.Client);
        var verifier = TestSignIn.NewVerifier();
        var start = await TestSignIn.BeginAsync(host.Client, client, "xyz", TestSignIn.Challenge(verifier));
        var clientRedirect = await TestSignIn.CallbackAsync(host.Client, await TestSignIn.FollowProviderAsync(start.Location!), start.ConsentCookie);
        var code = TestSignIn.Query(clientRedirect)["code"]!;

        using var first = await TestSignIn.ExchangeCodeAsync(host.Client, client, code, verifier);
        using var second = await TestSignIn.ExchangeCodeAsync(host.Client, client, code, verifier);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Old_refresh_token_is_rejected_after_rotation()
    {
        await using var stub = await StubOidcIssuer.StartAsync();
        await using var host = await GatewayTestHost.StartAsync(stub);
        var signedIn = await TestSignIn.SignInAsync(host.Client, stub, StubUser.For("pat@example.com"));

        using var first = await TestSignIn.RefreshAsync(host.Client, signedIn.Client, signedIn.RefreshToken);
        using var reused = await TestSignIn.RefreshAsync(host.Client, signedIn.Client, signedIn.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
    }

    [Fact]
    public async Task Wrong_client_secret_is_invalid_client()
    {
        await using var stub = await StubOidcIssuer.StartAsync();
        await using var host = await GatewayTestHost.StartAsync(stub);
        var signedIn = await TestSignIn.SignInAsync(host.Client, stub, StubUser.For("pat@example.com"));

        using var response = await host.Client.PostAsync("/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = signedIn.RefreshToken,
            ["client_id"] = signedIn.Client.ClientId,
            ["client_secret"] = "wrong",
        }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upstream_exchange_uses_client_secret_basic_and_pkce()
    {
        await using var stub = await StubOidcIssuer.StartAsync();
        await using var host = await GatewayTestHost.StartAsync(stub);

        await TestSignIn.SignInAsync(host.Client, stub, StubUser.For("pat@example.com"));

        var token = Assert.Single(stub.Requests, r => r.Path == "/token");
        Assert.StartsWith("Basic ", token.Authorization, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(token.Parameters["code_verifier"]));
        Assert.Equal($"{GatewayTestHost.BaseUrl}/oauth/callback", token.Parameters["redirect_uri"]);
    }
}
