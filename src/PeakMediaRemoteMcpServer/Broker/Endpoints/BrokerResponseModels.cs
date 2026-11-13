namespace PeakMediaRemoteMcpServer.Broker.Endpoints;

public sealed record OAuthErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string? ErrorDescription = null);

public sealed class AuthorizationServerMetadataResponse
{
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    [JsonPropertyName("authorization_endpoint")]
    public required string AuthorizationEndpoint { get; init; }

    [JsonPropertyName("token_endpoint")]
    public required string TokenEndpoint { get; init; }

    [JsonPropertyName("registration_endpoint")]
    public required string RegistrationEndpoint { get; init; }

    [JsonPropertyName("jwks_uri")]
    public required string JwksUri { get; init; }

    [JsonPropertyName("response_types_supported")]
    public required IReadOnlyList<string> ResponseTypesSupported { get; init; }

    [JsonPropertyName("grant_types_supported")]
    public required IReadOnlyList<string> GrantTypesSupported { get; init; }

    [JsonPropertyName("code_challenge_methods_supported")]
    public required IReadOnlyList<string> CodeChallengeMethodsSupported { get; init; }

    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public required IReadOnlyList<string> TokenEndpointAuthMethodsSupported { get; init; }

    [JsonPropertyName("authorization_response_iss_parameter_supported")]
    public required bool AuthorizationResponseIssParameterSupported { get; init; }
}

public sealed class JsonWebKeyResponse
{
    [JsonPropertyName("kty")]
    public required string Kty { get; init; }

    [JsonPropertyName("use")]
    public required string Use { get; init; }

    [JsonPropertyName("alg")]
    public required string Alg { get; init; }

    [JsonPropertyName("kid")]
    public required string Kid { get; init; }

    [JsonPropertyName("n")]
    public required string N { get; init; }

    [JsonPropertyName("e")]
    public required string E { get; init; }
}

public sealed class JsonWebKeySetResponse
{
    [JsonPropertyName("keys")]
    public required IReadOnlyList<JsonWebKeyResponse> Keys { get; init; }
}

/// <summary>RFC 7591 registration request (only the fields the gateway reads).</summary>
public sealed class RegistrationRequest
{
    [JsonPropertyName("redirect_uris")]
    public List<string>? RedirectUris { get; set; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; set; }

    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; set; }

    [JsonPropertyName("grant_types")]
    public List<string>? GrantTypes { get; set; }
}

/// <summary>RFC 7591 registration response. Carries a client secret, so a sealed class, never a record.</summary>
public sealed class RegistrationResponse
{
    [JsonPropertyName("client_id")]
    public required string ClientId { get; init; }

    [JsonPropertyName("client_secret")]
    public required string ClientSecret { get; init; }

    [JsonPropertyName("client_id_issued_at")]
    public required long ClientIdIssuedAt { get; init; }

    [JsonPropertyName("client_secret_expires_at")]
    public long ClientSecretExpiresAt { get; init; }

    [JsonPropertyName("redirect_uris")]
    public required IReadOnlyList<string> RedirectUris { get; init; }

    [JsonPropertyName("token_endpoint_auth_method")]
    public required string TokenEndpointAuthMethod { get; init; }

    [JsonPropertyName("grant_types")]
    public required IReadOnlyList<string> GrantTypes { get; init; }

    [JsonPropertyName("response_types")]
    public required IReadOnlyList<string> ResponseTypes { get; init; }

    [JsonPropertyName("client_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientName { get; init; }
}

/// <summary>Token response. Carries tokens, so a sealed class, never a record.</summary>
public sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }

    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    [JsonPropertyName("refresh_token")]
    public required string RefreshToken { get; init; }
}

internal static class OAuthResults
{
    public static IResult Error(string error, string? description = null, int status = StatusCodes.Status400BadRequest)
    {
        return Results.Json(new OAuthErrorResponse(error, description), statusCode: status);
    }

    /// <summary>Redirects to a validated client redirect URI with an OAuth error (RFC 6749 §4.1.2.1).</summary>
    public static IResult RedirectWithError(string redirectUri, string? state, string error)
    {
        var target = AppendQuery(redirectUri, ("error", error), ("state", string.IsNullOrEmpty(state) ? null : state));
        return Results.Redirect(target);
    }

    public static string AppendQuery(string uri, params (string Name, string? Value)[] parameters)
    {
        var builder = new StringBuilder(uri);
        var separator = uri.Contains('?') ? '&' : '?';
        foreach (var (name, value) in parameters)
        {
            if (value is null)
            {
                continue;
            }

            builder.Append(separator).Append(name).Append('=').Append(Uri.EscapeDataString(value));
            separator = '&';
        }

        return builder.ToString();
    }
}
