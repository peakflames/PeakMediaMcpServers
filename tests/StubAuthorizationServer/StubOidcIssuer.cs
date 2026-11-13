namespace PeakMediaStubAuthorizationServer;

/// <summary>A person the stub signs in.</summary>
public sealed record StubUser(string Subject, string Email, IReadOnlyList<string> Groups)
{
    /// <summary>A stable Okta-like subject derived from the email address.</summary>
    public static StubUser For(string email, params string[] groups)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(email)));
        return new StubUser($"00u{hash[..17]}", email, groups);
    }
}

/// <summary>One request the stub received, for assertions.</summary>
public sealed record StubRequest(string Method, string Path, IReadOnlyDictionary<string, string> Parameters, string? Authorization);

/// <summary>
/// Test double for the organization's OIDC provider (Okta), on real Kestrel at
/// <c>http://127.0.0.1:&lt;free port&gt;</c>. Serves discovery, JWKS, <c>/authorize</c> (auto-approves
/// the configured next user, or refuses with an OAuth error) and <c>/token</c> (checks the client
/// secret — <c>client_secret_basic</c> or <c>client_secret_post</c> — and PKCE, returns an RS256 ID token with <c>iss</c>, <c>aud</c>, <c>sub</c>, <c>email</c>,
/// <c>groups</c>, <c>nonce</c>, <c>exp</c>). Records every request. Tests never call real Okta.
/// </summary>
public sealed class StubOidcIssuer : IAsyncDisposable
{
    public const string DefaultClientId = "stub-okta-client";
    public const string DefaultClientSecret = "stub-okta-client-secret";

    private static readonly JsonWebTokenHandler Handler = new();

    private readonly WebApplication _app;
    private readonly RsaSecurityKey _signingKey;
    private readonly ConcurrentDictionary<string, PendingCode> _codes = new();
    private readonly ConcurrentQueue<StubRequest> _requests = new();
    private readonly Lock _gate = new();
    private StubUser _nextUser = StubUser.For("pat@example.com");
    private string? _refusal;

    private StubOidcIssuer(WebApplication app, string authority, string clientSecret)
    {
        _app = app;
        Authority = authority;
        ClientSecret = clientSecret;
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "stub-key-1" };
    }

    public string Authority { get; }

    public string ClientId { get; } = DefaultClientId;

    public string ClientSecret { get; }

    public string AuthorizationEndpoint => $"{Authority}/authorize";

    /// <summary>The ID-token claim the stub puts group names in (Okta federated claims cannot use <c>groups</c>).</summary>
    public string GroupsClaimName { get; set; } = "groups";

    public IReadOnlyList<StubRequest> Requests => [.. _requests];

    /// <summary>Starts the stub; <paramref name="clientSecret"/> is the client secret it expects at <c>/token</c>.</summary>
    public static async Task<StubOidcIssuer> StartAsync(string clientSecret = DefaultClientSecret)
    {
        var port = FreePortFor();
        var authority = $"http://127.0.0.1:{port}";
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(authority);
        var app = builder.Build();
        var stub = new StubOidcIssuer(app, authority, clientSecret);
        stub.Map(app);
        await app.StartAsync();
        return stub;
    }

    /// <summary>The person the next <c>/authorize</c> signs in.</summary>
    public void SetNextUser(StubUser user)
    {
        lock (_gate)
        {
            _nextUser = user;
            _refusal = null;
        }
    }

    /// <summary>The next <c>/authorize</c> redirects back with this OAuth error instead of a code.</summary>
    public void RefuseNext(string error)
    {
        lock (_gate)
        {
            _refusal = error;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var parameters = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
            if (context.Request.HasFormContentType)
            {
                foreach (var field in await context.Request.ReadFormAsync())
                {
                    parameters[field.Key] = field.Value.ToString();
                }
            }

            _requests.Enqueue(new StubRequest(context.Request.Method, context.Request.Path, parameters, context.Request.Headers.Authorization.ToString()));
            await next();
        });

        app.MapGet("/.well-known/openid-configuration", () => Results.Json(new Dictionary<string, object>
        {
            ["issuer"] = Authority,
            ["authorization_endpoint"] = AuthorizationEndpoint,
            ["token_endpoint"] = $"{Authority}/token",
            ["jwks_uri"] = $"{Authority}/jwks",
            ["response_types_supported"] = new[] { "code" },
            ["subject_types_supported"] = new[] { "public" },
            ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
            ["code_challenge_methods_supported"] = new[] { "S256" },
            ["token_endpoint_auth_methods_supported"] = new[] { "client_secret_basic", "client_secret_post" },
        }));

        app.MapGet("/jwks", () =>
        {
            var parameters = _signingKey.Rsa.ExportParameters(false);
            return Results.Json(new
            {
                keys = new[]
                {
                    new { kty = "RSA", use = "sig", alg = "RS256", kid = _signingKey.KeyId, n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!) },
                },
            });
        });

        app.MapGet("/authorize", (HttpRequest request) =>
        {
            var query = request.Query;
            var redirectUri = query["redirect_uri"].ToString();
            var state = query["state"].ToString();
            if (query["client_id"] != ClientId || string.IsNullOrEmpty(redirectUri))
            {
                return Results.BadRequest("unknown client or missing redirect_uri");
            }

            StubUser user;
            string? refusal;
            lock (_gate)
            {
                user = _nextUser;
                refusal = _refusal;
                _refusal = null;
            }

            if (refusal is not null)
            {
                return Results.Redirect($"{redirectUri}?error={Uri.EscapeDataString(refusal)}&state={Uri.EscapeDataString(state)}");
            }

            var code = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(24));
            _codes[code] = new PendingCode(user, redirectUri, query["nonce"].ToString(), query["code_challenge"].ToString());
            return Results.Redirect($"{redirectUri}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}");
        });

        app.MapPost("/token", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(ClientId)}:{Uri.EscapeDataString(ClientSecret)}"));
            var basicOk = request.Headers.Authorization.ToString() == expected;
            var postOk = string.IsNullOrEmpty(request.Headers.Authorization.ToString())
                && form["client_id"] == ClientId
                && form["client_secret"] == ClientSecret;
            if (!basicOk && !postOk)
            {
                return Results.Json(new { error = "invalid_client" }, statusCode: 401);
            }

            if (form["grant_type"] != "authorization_code" || !_codes.TryRemove(form["code"].ToString(), out var pending))
            {
                return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
            }

            var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString())));
            if (form["redirect_uri"] != pending.RedirectUri || challenge != pending.CodeChallenge)
            {
                return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
            }

            var now = DateTime.UtcNow;
            var claims = new Dictionary<string, object>
            {
                ["sub"] = pending.User.Subject,
                ["email"] = pending.User.Email,
                ["nonce"] = pending.Nonce,
                [GroupsClaimName] = pending.User.Groups.ToArray(),
            };
            var idToken = Handler.CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Authority,
                Audience = ClientId,
                IssuedAt = now,
                NotBefore = now,
                Expires = now.AddMinutes(5),
                Claims = claims,
                SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
            });
            return Results.Json(new { access_token = "stub-upstream-access-token", token_type = "Bearer", expires_in = 300, id_token = idToken });
        });
    }

    private static int FreePortFor()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed record PendingCode(StubUser User, string RedirectUri, string Nonce, string CodeChallenge);
}
