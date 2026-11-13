namespace PeakMediaRemoteMcpServer.Configuration;

/// <summary>
/// The <c>Broker</c> section: the gateway's own OAuth 2.1 authorization server and the upstream OIDC
/// provider (Okta) it delegates sign-in to. Holds a secret, so it is a <c>sealed class</c>, never a record.
/// </summary>
public sealed class BrokerOptions
{
    public const string SectionName = "Broker";

    /// <summary>The gateway's public base URL G (no trailing slash): token issuer and the base of every broker endpoint.</summary>
    public string IssuerUri { get; set; } = "http://localhost:5108";

    /// <summary>The OIDC provider's issuer, e.g. <c>https://example.okta.com</c>; discovery is read from its <c>/.well-known/openid-configuration</c>.</summary>
    public string UpstreamAuthority { get; set; } = string.Empty;

    public string UpstreamClientId { get; set; } = string.Empty;

    /// <summary>Arrives only through the <c>Broker__UpstreamClientSecret</c> environment variable.</summary>
    public string UpstreamClientSecret { get; set; } = string.Empty;

    /// <summary>Space-separated scopes requested from the OIDC provider.</summary>
    public string UpstreamScopes { get; set; } = "openid profile email groups";

    /// <summary>
    /// The ID-token claim holding the person's group names. Okta's newer federated-claims screen
    /// reserves <c>groups</c>, so a custom name (e.g. <c>peakmedia_groups</c>) can be configured here.
    /// </summary>
    public string UpstreamGroupsClaim { get; set; } = "groups";

    /// <summary>
    /// Non-loopback redirect URIs a dynamically registered client may use. Empty by default: DCR is
    /// open, so only loopback redirect URIs (any port) are accepted unless an operator lists more here.
    /// </summary>
    public List<string> AllowedRedirectUris { get; set; } = [];

    public int TransactionLifetimeMinutes { get; set; } = 15;

    public int ClientCodeLifetimeMinutes { get; set; } = 5;

    public int IssuedAccessTokenLifetimeMinutes { get; set; } = 60;

    public int IssuedRefreshTokenLifetimeDays { get; set; } = 30;

    public string NormalizedIssuerUri => IssuerUri.TrimEnd('/');

    public string CallbackUri => $"{NormalizedIssuerUri}/oauth/callback";
}
