namespace PeakMediaRemoteMcpServer.Broker.Endpoints;

/// <summary>
/// <c>GET /authorize</c>. Validates the client and its redirect URI first (an untrusted redirect URI
/// gets a bare 400 JSON error, never a redirect); then requires <c>response_type=code</c> and an S256
/// <c>code_challenge</c>, reporting failures to the client's redirect URI. On success stores a
/// transaction with the gateway's own upstream PKCE pair and nonce, sets the consent cookie, and
/// redirects the browser to the OIDC provider's authorization endpoint.
/// </summary>
public static class AuthorizeEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IOptions<BrokerOptions> brokerOptions,
        ClientStore clients,
        TransactionStore transactions,
        UpstreamOidcClient upstream,
        ILogger<UpstreamOidcClient> logger,
        CancellationToken cancellationToken)
    {
        var query = httpContext.Request.Query;
        var clientId = query["client_id"].ToString();
        var redirectUri = query["redirect_uri"].ToString();
        var state = query["state"].ToString();
        var codeChallenge = query["code_challenge"].ToString();
        var codeChallengeMethod = query["code_challenge_method"].ToString();

        if (string.IsNullOrWhiteSpace(clientId))
        {
            return OAuthResults.Error("invalid_request", "client_id is required. Register the client at /register first.");
        }

        var client = await clients.TryGetAsync(clientId, cancellationToken);
        if (client.IsFailed)
        {
            return OAuthResults.Error("server_error", "The client could not be looked up. Try again shortly.", StatusCodes.Status500InternalServerError);
        }

        if (client.Value is null)
        {
            return OAuthResults.Error("invalid_client", "Unknown client_id. Register the client at /register and retry.");
        }

        if (string.IsNullOrWhiteSpace(redirectUri) || !RedirectUriValidator.MatchesRegistered(redirectUri, client.Value.RedirectUris))
        {
            return OAuthResults.Error("invalid_request", "redirect_uri is missing or not registered for this client. Use a registered redirect URI.");
        }

        if (!string.Equals(query["response_type"].ToString(), "code", StringComparison.Ordinal))
        {
            return OAuthResults.RedirectWithError(redirectUri, state, "unsupported_response_type");
        }

        if (string.IsNullOrWhiteSpace(codeChallenge) || !string.Equals(codeChallengeMethod, "S256", StringComparison.Ordinal))
        {
            return OAuthResults.RedirectWithError(redirectUri, state, "invalid_request");
        }

        var configuration = await upstream.GetConfigurationAsync(cancellationToken);
        if (configuration.IsFailed)
        {
            return OAuthResults.RedirectWithError(redirectUri, state, "temporarily_unavailable");
        }

        var broker = brokerOptions.Value;
        var now = DateTimeOffset.UtcNow;
        var lifetime = TimeSpan.FromMinutes(broker.TransactionLifetimeMinutes);
        var txnId = PkceHelper.GenerateOpaqueSecret();
        var upstreamVerifier = PkceHelper.GenerateCodeVerifier();
        var nonce = PkceHelper.GenerateOpaqueSecret();
        var secureCookie = broker.NormalizedIssuerUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var consentSecret = ConsentCookie.Issue(httpContext, txnId, lifetime, secureCookie);

        var stored = await transactions.InsertAsync(
            new OAuthTransaction
            {
                TxnId = txnId,
                ClientId = clientId,
                ClientRedirectUri = redirectUri,
                ClientState = state,
                ClientCodeChallenge = codeChallenge,
                UpstreamCodeVerifier = upstreamVerifier,
                Nonce = nonce,
                ConsentTokenHash = TokenHashing.Hash(consentSecret),
                CreatedUtc = now,
                ExpiresUtc = now.Add(lifetime),
            },
            cancellationToken);
        if (stored.IsFailed)
        {
            logger.LogError("A sign-in attempt could not be stored: {Problem}", stored.Errors[0].Message);
            return OAuthResults.RedirectWithError(redirectUri, state, "server_error");
        }

        var target = OAuthResults.AppendQuery(
            configuration.Value.AuthorizationEndpoint,
            ("client_id", broker.UpstreamClientId),
            ("response_type", "code"),
            ("redirect_uri", broker.CallbackUri),
            ("scope", broker.UpstreamScopes),
            ("state", txnId),
            ("nonce", nonce),
            ("code_challenge", PkceHelper.ComputeS256Challenge(upstreamVerifier)),
            ("code_challenge_method", "S256"));
        return Results.Redirect(target);
    }
}
