namespace PeakMediaRemoteMcpServer.Broker.Endpoints;

/// <summary>Maps the authorization-server endpoints. All are anonymous — they are how a caller signs in.</summary>
public static class BrokerEndpoints
{
    public static void MapBrokerEndpoints(this WebApplication app)
    {
        app.MapGet("/.well-known/oauth-authorization-server", AuthorizationServerMetadataEndpoint.Handle).AllowAnonymous();
        app.MapGet("/.well-known/jwks.json", JwksEndpoint.Handle).AllowAnonymous();
        app.MapPost("/register", RegisterEndpoint.HandleAsync).AllowAnonymous();
        app.MapGet("/authorize", AuthorizeEndpoint.HandleAsync).AllowAnonymous();
        app.MapGet("/oauth/callback", CallbackEndpoint.HandleAsync).AllowAnonymous();
        app.MapPost("/token", TokenEndpoint.HandleAsync).AllowAnonymous().DisableAntiforgery();
    }
}
