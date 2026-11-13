namespace PeakMediaRemoteMcpServer.Configuration;

/// <summary>
/// Checks, before Kestrel binds, that the gateway is safe to open: sign-in is on, the sign-in
/// provider is fully configured, the shared OpenRouter key is present and the token audience matches
/// the gateway's public URL. Any failure means the gateway refuses to start — an open or
/// misconfigured gateway would spend the shared OpenRouter credit for anyone, or lock everyone out.
/// </summary>
public static class GatewayStartupValidator
{
    public const string McpAuthDisabledMessage =
        "McpAuth:Enabled is false. The gateway has no anonymous mode: set McpAuth:Enabled to true and configure the Broker settings, then restart.";

    public const string BrokerSecretMissingMessage =
        "Broker__UpstreamClientSecret is not set. Set the OIDC client secret of the organization's sign-in app in the environment and restart.";

    public const string OpenRouterKeyMissingMessage =
        "OpenRouter__ApiKey is not set. Set the organization's OpenRouter key in the environment and restart.";

    public const string UpstreamAuthorityMissingMessage =
        "Broker:UpstreamAuthority is not set. Set it to the OIDC provider's issuer URL (e.g. https://<org>.okta.com) and restart.";

    public const string UpstreamAuthorityInvalidMessage =
        "Broker:UpstreamAuthority must be an absolute https URL (plain http is allowed only for localhost). Correct it and restart.";

    public const string UpstreamClientIdMissingMessage =
        "Broker:UpstreamClientId is not set. Set the client ID of the organization's OIDC sign-in app and restart.";

    public const string IssuerUriInvalidMessage =
        "Broker:IssuerUri must be the gateway's absolute public URL (e.g. https://media.example.com). Correct it and restart.";

    public const string ResourceMismatchMessage =
        "McpAuth:Resource must equal Broker:IssuerUri followed by /mcp. Set both to the gateway's public URL and restart.";

    public static Result Validate(IConfiguration configuration)
    {
        var mcpAuth = configuration.GetSection(McpAuthOptions.SectionName).Get<McpAuthOptions>() ?? new McpAuthOptions();
        if (!mcpAuth.Enabled)
        {
            return Result.Fail(McpAuthDisabledMessage);
        }

        var broker = configuration.GetSection(BrokerOptions.SectionName).Get<BrokerOptions>() ?? new BrokerOptions();
        if (string.IsNullOrWhiteSpace(broker.UpstreamClientSecret))
        {
            return Result.Fail(BrokerSecretMissingMessage);
        }

        var openRouter = configuration.GetSection(OpenRouterOptions.SectionName).Get<OpenRouterOptions>() ?? new OpenRouterOptions();
        if (string.IsNullOrWhiteSpace(openRouter.ApiKey))
        {
            return Result.Fail(OpenRouterKeyMissingMessage);
        }

        if (string.IsNullOrWhiteSpace(broker.UpstreamAuthority))
        {
            return Result.Fail(UpstreamAuthorityMissingMessage);
        }

        if (!IsSecureOrLoopback(broker.UpstreamAuthority))
        {
            return Result.Fail(UpstreamAuthorityInvalidMessage);
        }

        if (string.IsNullOrWhiteSpace(broker.UpstreamClientId))
        {
            return Result.Fail(UpstreamClientIdMissingMessage);
        }

        if (!Uri.TryCreate(broker.IssuerUri, UriKind.Absolute, out var issuer)
            || (issuer.Scheme != Uri.UriSchemeHttp && issuer.Scheme != Uri.UriSchemeHttps))
        {
            return Result.Fail(IssuerUriInvalidMessage);
        }

        if (!string.Equals(mcpAuth.Resource.TrimEnd('/'), $"{broker.NormalizedIssuerUri}/mcp", StringComparison.Ordinal))
        {
            return Result.Fail(ResourceMismatchMessage);
        }

        return Result.Ok();
    }

    /// <summary>True for an absolute https URL, or an http URL whose host is loopback.</summary>
    public static bool IsSecureOrLoopback(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }
}
