namespace PeakMediaRemoteMcpServer.Broker.Endpoints;

/// <summary><c>GET /.well-known/oauth-authorization-server</c> (RFC 8414): the gateway as authorization server.</summary>
public static class AuthorizationServerMetadataEndpoint
{
    public static IResult Handle(IOptions<BrokerOptions> brokerOptions)
    {
        var issuer = brokerOptions.Value.NormalizedIssuerUri;
        return Results.Json(new AuthorizationServerMetadataResponse
        {
            Issuer = issuer,
            AuthorizationEndpoint = $"{issuer}/authorize",
            TokenEndpoint = $"{issuer}/token",
            RegistrationEndpoint = $"{issuer}/register",
            JwksUri = $"{issuer}/.well-known/jwks.json",
            ResponseTypesSupported = ["code"],
            GrantTypesSupported = ["authorization_code", "refresh_token"],
            CodeChallengeMethodsSupported = ["S256"],
            TokenEndpointAuthMethodsSupported = ["none", "client_secret_post", "client_secret_basic"],
            AuthorizationResponseIssParameterSupported = true,
        });
    }
}
