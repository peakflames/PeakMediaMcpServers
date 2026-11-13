namespace PeakMediaRemoteMcpServer.Broker.Endpoints;

/// <summary>
/// <c>POST /register</c> — dynamic client registration (RFC 7591), always on, so no client ID is ever
/// pre-shared. Every redirect URI must be loopback (any port) or listed in
/// <c>Broker:AllowedRedirectUris</c>. A client secret is issued even for <c>none</c> clients: some
/// clients (Claude Code) reject a registration response without one; PKCE protects the code either way.
/// </summary>
public static class RegisterEndpoint
{
    private static readonly string[] AllowedGrantTypes = ["authorization_code", "refresh_token"];
    private static readonly string[] AllowedAuthMethods = ["none", "client_secret_post", "client_secret_basic"];

    public static async Task<IResult> HandleAsync(
        HttpRequest request,
        IOptions<BrokerOptions> brokerOptions,
        ClientStore clients,
        ILogger<ClientStore> logger,
        CancellationToken cancellationToken)
    {
        RegistrationRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<RegistrationRequest>(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return OAuthResults.Error("invalid_client_metadata", "The body is not valid JSON. Send an RFC 7591 registration document.");
        }

        if (body?.RedirectUris is not { Count: > 0 } redirectUris)
        {
            return OAuthResults.Error("invalid_redirect_uri", "redirect_uris is required. Register at least one loopback redirect URI.");
        }

        var allowed = brokerOptions.Value.AllowedRedirectUris;
        if (redirectUris.Any(uri => !RedirectUriValidator.IsAllowedForRegistration(uri, allowed)))
        {
            return OAuthResults.Error(
                "invalid_redirect_uri",
                "Only loopback redirect URIs (http://127.0.0.1, http://localhost or http://[::1], any port) are accepted. Ask the gateway administrator to allow other redirect URIs.");
        }

        var grantTypes = body.GrantTypes is { Count: > 0 } requested ? requested : [.. AllowedGrantTypes];
        if (grantTypes.Except(AllowedGrantTypes).Any())
        {
            return OAuthResults.Error("invalid_client_metadata", "Only the authorization_code and refresh_token grants are supported.");
        }

        var authMethod = string.IsNullOrWhiteSpace(body.TokenEndpointAuthMethod) ? "client_secret_post" : body.TokenEndpointAuthMethod;
        if (!AllowedAuthMethods.Contains(authMethod))
        {
            return OAuthResults.Error("invalid_client_metadata", "token_endpoint_auth_method must be none, client_secret_post or client_secret_basic.");
        }

        var clientSecret = PkceHelper.GenerateOpaqueSecret();
        var now = DateTimeOffset.UtcNow;
        var client = new RegisteredClient(Guid.NewGuid().ToString("N"), TokenHashing.Hash(clientSecret), redirectUris, body.ClientName, now);
        var stored = await clients.InsertAsync(client, cancellationToken);
        if (stored.IsFailed)
        {
            logger.LogError("A client registration could not be stored: {Problem}", stored.Errors[0].Message);
            return OAuthResults.Error("server_error", "The registration could not be stored. Try again shortly.", StatusCodes.Status500InternalServerError);
        }

        return Results.Json(
            new RegistrationResponse
            {
                ClientId = client.ClientId,
                ClientSecret = clientSecret,
                ClientIdIssuedAt = now.ToUnixTimeSeconds(),
                RedirectUris = redirectUris,
                TokenEndpointAuthMethod = authMethod,
                GrantTypes = grantTypes,
                ResponseTypes = ["code"],
                ClientName = body.ClientName,
            },
            statusCode: StatusCodes.Status201Created);
    }
}
