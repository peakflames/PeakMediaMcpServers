namespace PeakMediaRemoteMcpServer.Tests;

/// <summary>A client registered through <c>POST /register</c>.</summary>
public sealed record TestClient(string ClientId, string ClientSecret, string RedirectUri);

/// <summary>The gateway's answer to <c>/authorize</c>.</summary>
public sealed record AuthorizationStart(HttpStatusCode Status, Uri? Location, string? ConsentCookie, string Body);

/// <summary>Tokens from a completed sign-in. A sealed class (holds tokens).</summary>
public sealed class SignedIn
{
    public required string AccessToken { get; init; }

    public required string RefreshToken { get; init; }

    public required TestClient Client { get; init; }
}

/// <summary>
/// Signs in through the real broker flow — <c>/register</c> → <c>/authorize</c> → stub OIDC
/// <c>/authorize</c> → gateway <c>/oauth/callback</c> → client redirect → <c>/token</c> — exactly as
/// an MCP client and a browser would. This is the only way tests obtain a gateway access token;
/// there is no bypass. The gateway client must not follow redirects or keep cookies (the consent
/// cookie is carried by hand, like a browser would).
/// </summary>
public static class TestSignIn
{
    public const string AdminGroup = "PeakMedia Admins";
    public const string ClientRedirectUri = "http://127.0.0.1:33418/callback";

    public const string InitializeBody =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""";

    private static readonly HttpClient Browser = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    public static async Task<SignedIn> SignInAsync(HttpClient gateway, StubOidcIssuer stub, StubUser user)
    {
        stub.SetNextUser(user);
        var client = await RegisterAsync(gateway);
        var verifier = NewVerifier();
        var start = await BeginAsync(gateway, client, "xyz", Challenge(verifier));
        Assert.Equal(HttpStatusCode.Redirect, start.Status);
        var callback = await FollowProviderAsync(start.Location!);
        var clientRedirect = await CallbackAsync(gateway, callback, start.ConsentCookie);
        var code = Query(clientRedirect)["code"];
        using var tokenResponse = await ExchangeCodeAsync(gateway, client, code!, verifier);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var tokens = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        return new SignedIn
        {
            AccessToken = tokens.RootElement.GetProperty("access_token").GetString()!,
            RefreshToken = tokens.RootElement.GetProperty("refresh_token").GetString()!,
            Client = client,
        };
    }

    public static async Task<TestClient> RegisterAsync(HttpClient gateway, string redirectUri = ClientRedirectUri)
    {
        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["redirect_uris"] = new[] { redirectUri },
            ["client_name"] = "test client",
            ["token_endpoint_auth_method"] = "none",
        });
        using var response = await gateway.PostAsync("/register", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new TestClient(
            document.RootElement.GetProperty("client_id").GetString()!,
            document.RootElement.GetProperty("client_secret").GetString()!,
            redirectUri);
    }

    /// <summary>GET /authorize; <paramref name="codeChallenge"/> null omits PKCE.</summary>
    public static async Task<AuthorizationStart> BeginAsync(HttpClient gateway, TestClient client, string state, string? codeChallenge)
    {
        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = client.ClientId,
            ["redirect_uri"] = client.RedirectUri,
            ["state"] = state,
        };
        if (codeChallenge is not null)
        {
            query["code_challenge"] = codeChallenge;
            query["code_challenge_method"] = "S256";
        }

        using var response = await gateway.GetAsync(QueryHelpers.AddQueryString("/authorize", query));
        var cookie = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Select(c => c.Split(';')[0]).FirstOrDefault(c => c.StartsWith("pm_consent=", StringComparison.Ordinal))
            : null;
        return new AuthorizationStart(response.StatusCode, response.Headers.Location, cookie, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The browser at the OIDC provider: returns where the provider redirects (the gateway callback).</summary>
    public static async Task<Uri> FollowProviderAsync(Uri providerAuthorizeUrl)
    {
        using var response = await Browser.GetAsync(providerAuthorizeUrl);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!;
    }

    /// <summary>The browser at the gateway callback, carrying the consent cookie; returns the client redirect.</summary>
    public static async Task<Uri> CallbackAsync(HttpClient gateway, Uri callbackUrl, string? consentCookie)
    {
        using var response = await CallbackRawAsync(gateway, callbackUrl, consentCookie);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!;
    }

    public static async Task<HttpResponseMessage> CallbackRawAsync(HttpClient gateway, Uri callbackUrl, string? consentCookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, callbackUrl.PathAndQuery);
        if (consentCookie is not null)
        {
            request.Headers.Add("Cookie", consentCookie);
        }

        return await gateway.SendAsync(request);
    }

    public static Task<HttpResponseMessage> ExchangeCodeAsync(HttpClient gateway, TestClient client, string code, string verifier)
    {
        return gateway.PostAsync("/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = client.RedirectUri,
            ["client_id"] = client.ClientId,
            ["code_verifier"] = verifier,
        }));
    }

    public static Task<HttpResponseMessage> RefreshAsync(HttpClient gateway, TestClient client, string refreshToken)
    {
        return gateway.PostAsync("/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = client.ClientId,
        }));
    }

    /// <summary>POST an MCP initialize to /mcp, with the bearer token when given.</summary>
    public static async Task<HttpResponseMessage> InitializeAsync(HttpClient gateway, string? accessToken, string path = "/mcp")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(InitializeBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await gateway.SendAsync(request);
    }

    /// <summary>Reads a JSON-RPC message from a JSON or server-sent-events response body.</summary>
    public static async Task<JsonDocument> ReadJsonRpcAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        var data = text.Split('\n').FirstOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal));
        return JsonDocument.Parse(data is null ? text : data["data:".Length..].Trim());
    }

    public static string NewVerifier()
    {
        return Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    }

    public static string Challenge(string verifier)
    {
        return Base64UrlTextEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    public static Dictionary<string, string?> Query(Uri uri)
    {
        return QueryHelpers.ParseQuery(uri.Query).ToDictionary(q => q.Key, q => (string?)q.Value.ToString());
    }
}
