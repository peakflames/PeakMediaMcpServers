namespace PeakMediaRemoteMcpServer.Broker.Endpoints;

/// <summary>
/// <c>POST /token</c>: <c>authorization_code</c> (code consumed first, then client, redirect URI and
/// the client's PKCE verifier checked — any mismatch is <c>invalid_grant</c> with no token) and
/// <c>refresh_token</c> (the gateway's own refresh token, rotated on every use; role and email re-read
/// from <c>users</c>; the OIDC provider is not re-contacted). Client authentication: <c>none</c>,
/// <c>client_secret_post</c> or <c>client_secret_basic</c>; a presented secret is always verified.
/// </summary>
public static class TokenEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpRequest request,
        IOptions<BrokerOptions> brokerOptions,
        ClientStore clients,
        CodeStore codes,
        RefreshTokenStore refreshTokens,
        UserStore users,
        JwtIssuer jwtIssuer,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return OAuthResults.Error("invalid_request", "Send the token request as application/x-www-form-urlencoded.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var authenticated = await AuthenticateClientAsync(request, form, clients, cancellationToken);
        if (authenticated.IsFailed)
        {
            return OAuthResults.Error("invalid_client", "Client authentication failed. Register the client again and retry.", StatusCodes.Status401Unauthorized);
        }

        var clientId = authenticated.Value;
        var context = new TokenContext(brokerOptions.Value, codes, refreshTokens, users, jwtIssuer);
        return form["grant_type"].ToString() switch
        {
            "authorization_code" => await HandleAuthorizationCodeAsync(form, clientId, context, cancellationToken),
            "refresh_token" => await HandleRefreshTokenAsync(form, clientId, context, cancellationToken),
            _ => OAuthResults.Error("unsupported_grant_type", "Use grant_type authorization_code or refresh_token."),
        };
    }

    private static async Task<IResult> HandleAuthorizationCodeAsync(IFormCollection form, string? clientId, TokenContext context, CancellationToken cancellationToken)
    {
        var code = form["code"].ToString();
        var codeVerifier = form["code_verifier"].ToString();
        var redirectUri = form["redirect_uri"].ToString();
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(codeVerifier) || string.IsNullOrEmpty(clientId))
        {
            return OAuthResults.Error("invalid_request", "code, code_verifier and client_id are required.");
        }

        // Consumed before any further check: a code is never usable twice, even after a failed attempt.
        var consumed = await context.Codes.TryConsumeAsync(code, cancellationToken);
        if (consumed.IsFailed)
        {
            return ServerError();
        }

        var record = consumed.Value;
        if (record is null
            || !string.Equals(clientId, record.ClientId, StringComparison.Ordinal)
            || (!string.IsNullOrEmpty(redirectUri) && !string.Equals(redirectUri, record.ClientRedirectUri, StringComparison.Ordinal))
            || !PkceHelper.VerifyS256(codeVerifier, record.ClientCodeChallenge))
        {
            return InvalidGrant();
        }

        return await IssueAsync(record.Subject, record.ClientId, context, cancellationToken);
    }

    private static async Task<IResult> HandleRefreshTokenAsync(IFormCollection form, string? clientId, TokenContext context, CancellationToken cancellationToken)
    {
        var refreshToken = form["refresh_token"].ToString();
        if (string.IsNullOrEmpty(refreshToken))
        {
            return OAuthResults.Error("invalid_request", "refresh_token is required.");
        }

        var consumed = await context.RefreshTokens.TryConsumeAsync(refreshToken, cancellationToken);
        if (consumed.IsFailed)
        {
            return ServerError();
        }

        var record = consumed.Value;
        if (record is null || (!string.IsNullOrEmpty(clientId) && !string.Equals(clientId, record.ClientId, StringComparison.Ordinal)))
        {
            return InvalidGrant();
        }

        return await IssueAsync(record.Subject, record.ClientId, context, cancellationToken);
    }

    private static async Task<IResult> IssueAsync(string subject, string clientId, TokenContext context, CancellationToken cancellationToken)
    {
        var user = await context.Users.TryGetAsync(subject, cancellationToken);
        if (user.IsFailed)
        {
            return ServerError();
        }

        if (user.Value is null)
        {
            return InvalidGrant();
        }

        var now = DateTimeOffset.UtcNow;
        var refreshToken = PkceHelper.GenerateOpaqueSecret();
        var stored = await context.RefreshTokens.InsertAsync(
            refreshToken,
            new RefreshTokenRecord(subject, clientId),
            now.AddDays(context.Broker.IssuedRefreshTokenLifetimeDays),
            cancellationToken);
        if (stored.IsFailed)
        {
            return ServerError();
        }

        await context.Users.TouchAsync(subject, now, cancellationToken);
        return Results.Json(new TokenResponse
        {
            AccessToken = context.JwtIssuer.IssueAccessToken(subject, user.Value.Email, user.Value.IsAdmin, now),
            TokenType = "Bearer",
            ExpiresIn = context.JwtIssuer.LifetimeSeconds,
            RefreshToken = refreshToken,
        });
    }

    /// <summary>
    /// Returns the authenticated client ID (or the claimed one for a public client), or a failure when
    /// a presented secret is wrong, the client is unknown, or two different client IDs are presented.
    /// </summary>
    private static async Task<Result<string?>> AuthenticateClientAsync(HttpRequest request, IFormCollection form, ClientStore clients, CancellationToken cancellationToken)
    {
        var formClientId = form["client_id"].ToString();
        var formSecret = form["client_secret"].ToString();
        string? basicId = null;
        string? basicSecret = null;

        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..].Trim()));
                var colon = decoded.IndexOf(':', StringComparison.Ordinal);
                if (colon < 0)
                {
                    return Result.Fail("malformed basic credentials");
                }

                basicId = Uri.UnescapeDataString(decoded[..colon]);
                basicSecret = Uri.UnescapeDataString(decoded[(colon + 1)..]);
            }
            catch (FormatException)
            {
                return Result.Fail("malformed basic credentials");
            }
        }

        if (basicId is not null && !string.IsNullOrEmpty(formClientId) && !string.Equals(basicId, formClientId, StringComparison.Ordinal))
        {
            return Result.Fail("conflicting client IDs");
        }

        var clientId = basicId ?? (string.IsNullOrEmpty(formClientId) ? null : formClientId);
        var secret = basicSecret ?? (string.IsNullOrEmpty(formSecret) ? null : formSecret);
        if (clientId is null)
        {
            return Result.Ok<string?>(null);
        }

        var client = await clients.TryGetAsync(clientId, cancellationToken);
        if (client.IsFailed || client.Value is null)
        {
            return Result.Fail("unknown client");
        }

        if (secret is not null && !TokenHashing.Verify(secret, client.Value.ClientSecretHash))
        {
            return Result.Fail("wrong client secret");
        }

        return Result.Ok<string?>(clientId);
    }

    private static IResult InvalidGrant()
    {
        return OAuthResults.Error("invalid_grant", "The code or refresh token is invalid, expired or already used. Sign in again from your MCP client.");
    }

    private static IResult ServerError()
    {
        return OAuthResults.Error("server_error", "The token could not be issued. Try again shortly.", StatusCodes.Status500InternalServerError);
    }

    private sealed record TokenContext(BrokerOptions Broker, CodeStore Codes, RefreshTokenStore RefreshTokens, UserStore Users, JwtIssuer JwtIssuer);
}
