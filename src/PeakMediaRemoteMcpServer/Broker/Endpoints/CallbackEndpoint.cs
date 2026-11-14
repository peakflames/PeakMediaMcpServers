namespace PeakMediaRemoteMcpServer.Broker.Endpoints;

/// <summary>
/// <c>GET /oauth/callback</c> — the OIDC provider returns here with the transaction ID as
/// <c>state</c>. Unknown state → 400; consent-cookie mismatch → 403; a provider <c>error</c> (e.g.
/// <c>access_denied</c> for a person not assigned to the app) is passed to the client's redirect URI
/// with nothing stored. On success the ID token is validated, the person is recorded in <c>users</c>
/// (<c>admin</c> when the <c>groups</c> claim contains <c>Admin:Group</c>), and a hashed one-time code
/// bound to the client, redirect URI and PKCE challenge is issued to the client.
/// </summary>
public static class CallbackEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IOptions<BrokerOptions> brokerOptions,
        IOptions<AdminOptions> adminOptions,
        TransactionStore transactions,
        CodeStore codes,
        UserStore users,
        UpstreamOidcClient upstream,
        ILogger<UpstreamOidcClient> logger,
        CancellationToken cancellationToken)
    {
        var query = httpContext.Request.Query;
        var state = query["state"].ToString();
        if (string.IsNullOrWhiteSpace(state))
        {
            return OAuthResults.Error("invalid_request", "state is missing. Start the sign-in again from your MCP client.");
        }

        var found = await transactions.TryGetAsync(state, cancellationToken);
        if (found.IsFailed)
        {
            return OAuthResults.Error("server_error", "The sign-in attempt could not be read. Start the sign-in again shortly.", StatusCodes.Status500InternalServerError);
        }

        var transaction = found.Value;
        if (transaction is null)
        {
            return OAuthResults.Error("invalid_request", "Unknown or expired sign-in attempt. Start the sign-in again from your MCP client.");
        }

        // Single use from here on, whatever the outcome.
        await transactions.DeleteAsync(transaction.TxnId, cancellationToken);

        if (!ConsentCookie.Validate(httpContext, transaction.TxnId, transaction.ConsentTokenHash))
        {
            return OAuthResults.Error("access_denied", "This sign-in was started in a different browser. Start the sign-in again from your MCP client.", StatusCodes.Status403Forbidden);
        }

        ConsentCookie.Clear(httpContext);

        var error = query["error"].ToString();
        if (!string.IsNullOrWhiteSpace(error))
        {
            logger.LogInformation("The OIDC provider refused a sign-in: {Error}.", SafeErrorCode(error));
            return OAuthResults.RedirectWithError(transaction.ClientRedirectUri, transaction.ClientState, SafeErrorCode(error));
        }

        var code = query["code"].ToString();
        if (string.IsNullOrWhiteSpace(code))
        {
            return OAuthResults.RedirectWithError(transaction.ClientRedirectUri, transaction.ClientState, "invalid_request");
        }

        var identity = await upstream.ExchangeCodeAsync(code, transaction.UpstreamCodeVerifier, transaction.Nonce, cancellationToken);
        if (identity.IsFailed)
        {
            logger.LogWarning("A sign-in could not be completed: {Problem}.", identity.Errors[0].Message);
            return OAuthResults.RedirectWithError(transaction.ClientRedirectUri, transaction.ClientState, "server_error");
        }

        var person = identity.Value;
        var adminGroup = adminOptions.Value.Group;
        var isAdmin = AdminGroup.Grants(person.Groups, adminGroup);
        var now = DateTimeOffset.UtcNow;

        var recorded = await users.UpsertSignInAsync(person.Subject, person.Email, isAdmin, now, cancellationToken);
        var clientCode = PkceHelper.GenerateOpaqueSecret();
        var issued = recorded.IsFailed
            ? recorded
            : await codes.InsertAsync(
                clientCode,
                new AuthorizationCodeRecord(person.Subject, transaction.ClientId, transaction.ClientRedirectUri, transaction.ClientCodeChallenge),
                now.AddMinutes(brokerOptions.Value.ClientCodeLifetimeMinutes),
                cancellationToken);
        if (issued.IsFailed)
        {
            logger.LogError("A sign-in could not be recorded: {Problem}", issued.Errors[0].Message);
            return OAuthResults.RedirectWithError(transaction.ClientRedirectUri, transaction.ClientState, "server_error");
        }

        logger.LogInformation("Sign-in completed with role {Role}.", isAdmin ? JwtIssuer.AdminRole : JwtIssuer.UserRole);
        logger.LogDebug("Sign-in completed for subject {Subject}.", person.Subject);
        logger.LogDebug(
            "The ID token carried {GroupCount} group claim(s); Admin:Group is {AdminGroupState}.",
            person.Groups.Count,
            string.IsNullOrWhiteSpace(adminGroup) ? "not set" : isAdmin ? "among them" : "not among them");

        var target = OAuthResults.AppendQuery(
            transaction.ClientRedirectUri,
            ("code", clientCode),
            ("state", string.IsNullOrEmpty(transaction.ClientState) ? null : transaction.ClientState),
            ("iss", brokerOptions.Value.NormalizedIssuerUri));
        return Results.Redirect(target);
    }

    /// <summary>Passes a provider error code through only when it is a plain OAuth error token.</summary>
    private static string SafeErrorCode(string error)
    {
        return error.Length <= 64 && error.All(c => char.IsAsciiLetterLower(c) || c == '_') ? error : "access_denied";
    }
}
