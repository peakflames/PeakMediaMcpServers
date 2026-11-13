namespace PeakMediaRemoteMcpServer.Configuration;

/// <summary>
/// Checks, before Kestrel binds, that the gateway is safe to open: sign-in is on, the sign-in
/// provider's client secret is present and the shared OpenRouter key is present. Any failure means
/// the gateway refuses to start — an open gateway would spend the shared OpenRouter credit for anyone.
/// </summary>
public static class GatewayStartupValidator
{
    public const string McpAuthDisabledMessage =
        "McpAuth:Enabled is false. The gateway has no anonymous mode: set McpAuth:Enabled to true and configure the Broker settings, then restart.";

    public const string BrokerSecretMissingMessage =
        "Broker__UpstreamClientSecret is not set. Set the OIDC client secret of the organization's sign-in app in the environment and restart.";

    public const string OpenRouterKeyMissingMessage =
        "OpenRouter__ApiKey is not set. Set the organization's OpenRouter key in the environment and restart.";

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

        return Result.Ok();
    }
}
