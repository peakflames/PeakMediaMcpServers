namespace PeakMediaRemoteMcpServer.Broker.Endpoints;

/// <summary><c>GET /.well-known/jwks.json</c>: the public half of the token-signing key, for external verifiers.</summary>
public static class JwksEndpoint
{
    public static IResult Handle(SigningKeyProvider signingKey)
    {
        var parameters = signingKey.Key.Rsa.ExportParameters(includePrivateParameters: false);
        return Results.Json(new JsonWebKeySetResponse
        {
            Keys =
            [
                new JsonWebKeyResponse
                {
                    Kty = "RSA",
                    Use = "sig",
                    Alg = "RS256",
                    Kid = signingKey.Key.KeyId,
                    N = Base64UrlEncoder.Encode(parameters.Modulus!),
                    E = Base64UrlEncoder.Encode(parameters.Exponent!),
                },
            ],
        });
    }
}
