namespace PeakMediaRemoteMcpServer.Tests.Broker;

public class AuthorizeAndCallbackTests
{
    // TOR-02-PosyPLa: /authorize with client, redirect URI, state and S256 challenge → 302 to the OIDC
    // provider's authorization endpoint with redirect_uri = G/oauth/callback.
    [Fact]
    public async Task TOR_02_PosyPLa_authorize_redirects_to_oidc_sign_in()
    {
        await using var stub = await StubOidcIssuer.StartAsync();
        await using var host = await GatewayTestHost.StartAsync(stub);
        var client = await TestSignIn.RegisterAsync(host.Client);

        var start = await TestSignIn.BeginAsync(host.Client, client, "xyz", TestSignIn.Challenge(TestSignIn.NewVerifier()));

        Assert.Equal(HttpStatusCode.Redirect, start.Status);
        Assert.StartsWith(stub.AuthorizationEndpoint, start.Location!.ToString(), StringComparison.Ordinal);
        var query = TestSignIn.Query(start.Location);
        Assert.Equal($"{GatewayTestHost.BaseUrl}/oauth/callback", query["redirect_uri"]);
        Assert.Equal(stub.ClientId, query["client_id"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrEmpty(query["nonce"]));
    }

    // TOR-02-DU3EQVF: /authorize without code_challenge → not redirected to the provider; OAuth error invalid_request.
    [Fact]
    public async Task TOR_02_DU3EQVF_authorize_without_code_challenge_is_invalid_request()
    {
        await using var stub = await StubOidcIssuer.StartAsync();
        await using var host = await GatewayTestHost.StartAsync(stub);
        var client = await TestSignIn.RegisterAsync(host.Client);

        var start = await TestSignIn.BeginAsync(host.Client, client, "xyz", codeChallenge: null);

        Assert.DoesNotContain(stub.Requests, r => r.Path == "/authorize");
        Assert.NotNull(start.Location);
        Assert.False(start.Location!.ToString().StartsWith(stub.Authority, StringComparison.Ordinal));
        Assert.StartsWith(client.RedirectUri, start.Location.ToString(), StringComparison.Ordinal);
        Assert.Equal("invalid_request", TestSignIn.Query(start.Location)["error"]);
    }

    // TOR-02-KmwGCjA: the provider refuses (access_denied) → client redirect carries error=access_denied and no
    // code; no code, refresh token or user row exists for the person.
    [Fact]
    public async Task TOR_02_KmwGCjA_refused_sign_in_issues_no_code_or_token()
    {
        await using var stub = await StubOidcIssuer.StartAsync();
        await using var host = await GatewayTestHost.StartAsync(stub);
        var person = StubUser.For("unassigned@example.com");
        stub.SetNextUser(person);
        stub.RefuseNext("access_denied");
        var client = await TestSignIn.RegisterAsync(host.Client);
        var start = await TestSignIn.BeginAsync(host.Client, client, "xyz", TestSignIn.Challenge(TestSignIn.NewVerifier()));

        var callback = await TestSignIn.FollowProviderAsync(start.Location!);
        Assert.Equal("access_denied", TestSignIn.Query(callback)["error"]);
        var clientRedirect = await TestSignIn.CallbackAsync(host.Client, callback, start.ConsentCookie);

        Assert.StartsWith(client.RedirectUri, clientRedirect.ToString(), StringComparison.Ordinal);
        var query = TestSignIn.Query(clientRedirect);
        Assert.Equal("access_denied", query["error"]);
        Assert.Equal("xyz", query["state"]);
        Assert.False(query.ContainsKey("code"));
        Assert.Equal(0, await host.CountRowsAsync("oauth_codes"));
        Assert.Equal(0, await host.CountRowsAsync("oauth_refresh_tokens"));
        Assert.Equal(0, await host.CountRowsAsync("users", person.Subject));
        Assert.DoesNotContain(stub.Requests, r => r.Path == "/token");
    }

    [Fact]
    public async Task Callback_without_the_consent_cookie_is_403()
    {
        await using var stub = await StubOidcIssuer.StartAsync();
        await using var host = await GatewayTestHost.StartAsync(stub);
        var client = await TestSignIn.RegisterAsync(host.Client);
        var start = await TestSignIn.BeginAsync(host.Client, client, "xyz", TestSignIn.Challenge(TestSignIn.NewVerifier()));
        var callback = await TestSignIn.FollowProviderAsync(start.Location!);

        using var response = await TestSignIn.CallbackRawAsync(host.Client, callback, consentCookie: "pm_consent=someone-else.secret");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await host.CountRowsAsync("oauth_codes"));
    }

    [Fact]
    public async Task Callback_with_unknown_state_is_400()
    {
        await using var host = await GatewayTestHost.StartAsync();

        using var response = await host.Client.GetAsync("/oauth/callback?code=abc&state=unknown");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_with_unregistered_redirect_is_a_bare_400_not_a_redirect()
    {
        await using var host = await GatewayTestHost.StartAsync();
        var client = await TestSignIn.RegisterAsync(host.Client);

        var start = await TestSignIn.BeginAsync(host.Client, client with { RedirectUri = "https://evil.example.com/cb" }, "xyz", "challenge");

        Assert.Equal(HttpStatusCode.BadRequest, start.Status);
        Assert.Null(start.Location);
    }
}
