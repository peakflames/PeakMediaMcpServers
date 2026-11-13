// Test sign-in helper for `build.py smoke`: runs the stub OIDC issuer (in place of Okta) and signs in to a
// running gateway through its real broker flow — /register → /authorize (PKCE) → stub /authorize → gateway
// /oauth/callback → /token — exactly as an MCP client and a browser would. No bypass: the gateway issues the
// token itself. Line protocol on stdio, driven by build.py:
//   stdout: authority=<url>, client_id=<id>, client_secret=<stub test constant>
//   stdin:  signin <gateway base url>
//   stdout: token=<gateway access token>   or   error=<step>: HTTP <code>   (then exit 1)
// The stub stays up until stdin closes (the gateway calls it at sign-in), then the tool exits 0.
// The token goes to build.py through the pipe only; nothing here logs it.

const string ClientRedirectUri = "http://127.0.0.1:33418/callback";
const string SmokeUser = "smoke@example.com";

await using var stub = await StubOidcIssuer.StartAsync();
stub.SetNextUser(StubUser.For(SmokeUser));
Console.WriteLine($"authority={stub.Authority}");
Console.WriteLine($"client_id={stub.ClientId}");
Console.WriteLine($"client_secret={stub.ClientSecret}");

var exitCode = 0;
string? line;
while ((line = await Console.In.ReadLineAsync()) is not null)
{
    if (!line.StartsWith("signin ", StringComparison.Ordinal))
    {
        continue;
    }

    var outcome = await SignInAsync(line["signin ".Length..].Trim());
    Console.WriteLine(outcome.IsSuccess ? $"token={outcome.Value}" : $"error={outcome.Error}");
    if (!outcome.IsSuccess)
    {
        exitCode = 1;
        break;
    }
}

return exitCode;

static async Task<Outcome> SignInAsync(string gatewayBaseUrl)
{
    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        BaseAddress = new Uri(gatewayBaseUrl),
        Timeout = TimeSpan.FromSeconds(15),
    };

    try
    {
        // 1. Dynamic client registration with a loopback redirect, like an MCP client.
        var registration = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["redirect_uris"] = new[] { ClientRedirectUri },
            ["client_name"] = "build.py smoke",
            ["token_endpoint_auth_method"] = "none",
        });
        using var registered = await http.PostAsync("/register", new StringContent(registration, Encoding.UTF8, "application/json"));
        if (registered.StatusCode != HttpStatusCode.Created)
        {
            return Outcome.Fail("register", registered.StatusCode);
        }

        using var client = JsonDocument.Parse(await registered.Content.ReadAsStringAsync());
        var clientId = client.RootElement.GetProperty("client_id").GetString()!;

        // 2. Gateway /authorize with PKCE: redirects to the provider and sets the consent cookie.
        var verifier = Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlTextEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorizeUrl = QueryHelpers.AddQueryString("/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = ClientRedirectUri,
            ["state"] = "smoke",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        });
        using var authorize = await http.GetAsync(authorizeUrl);
        if (authorize.StatusCode != HttpStatusCode.Redirect || authorize.Headers.Location is null)
        {
            return Outcome.Fail("authorize", authorize.StatusCode);
        }

        var consentCookie = authorize.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Select(c => c.Split(';')[0]).FirstOrDefault(c => c.StartsWith("pm_consent=", StringComparison.Ordinal))
            : null;

        // 3. The "browser" at the provider (the stub auto-approves the smoke user).
        using var provider = await http.GetAsync(authorize.Headers.Location);
        if (provider.StatusCode != HttpStatusCode.Redirect || provider.Headers.Location is null)
        {
            return Outcome.Fail("provider authorize", provider.StatusCode);
        }

        // 4. The "browser" back at the gateway callback, carrying the consent cookie.
        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, provider.Headers.Location.PathAndQuery);
        if (consentCookie is not null)
        {
            callbackRequest.Headers.Add("Cookie", consentCookie);
        }

        using var callback = await http.SendAsync(callbackRequest);
        if (callback.StatusCode != HttpStatusCode.Redirect || callback.Headers.Location is null)
        {
            return Outcome.Fail("callback", callback.StatusCode);
        }

        var code = QueryHelpers.ParseQuery(callback.Headers.Location.Query)["code"].ToString();

        // 5. Code exchange at the gateway /token.
        using var token = await http.PostAsync("/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = ClientRedirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier,
        }));
        if (token.StatusCode != HttpStatusCode.OK)
        {
            return Outcome.Fail("token", token.StatusCode);
        }

        using var tokens = JsonDocument.Parse(await token.Content.ReadAsStringAsync());
        return Outcome.Ok(tokens.RootElement.GetProperty("access_token").GetString()!);
    }
    catch (HttpRequestException)
    {
        return new Outcome(null, $"connect: the gateway at {gatewayBaseUrl} did not answer");
    }
    catch (TaskCanceledException)
    {
        return new Outcome(null, $"timeout: the gateway at {gatewayBaseUrl} did not answer in time");
    }
}

/// <summary>A sign-in result. A sealed class, not a record: it holds an access token.</summary>
internal sealed class Outcome(string? value, string? error)
{
    public string? Value { get; } = value;

    public string? Error { get; } = error;

    public bool IsSuccess => Value is not null;

    public static Outcome Ok(string value)
    {
        return new Outcome(value, null);
    }

    public static Outcome Fail(string step, HttpStatusCode status)
    {
        return new Outcome(null, $"{step}: HTTP {(int)status}");
    }
}
