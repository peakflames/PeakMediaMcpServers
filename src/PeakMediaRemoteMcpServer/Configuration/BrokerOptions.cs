namespace PeakMediaRemoteMcpServer.Configuration;

/// <summary>
/// The <c>Broker</c> section: the upstream OIDC provider (Okta) the gateway's authorization
/// server delegates sign-in to. Holds a secret, so it is a <c>sealed class</c>, never a record.
/// </summary>
public sealed class BrokerOptions
{
    public const string SectionName = "Broker";

    public string UpstreamAuthority { get; set; } = string.Empty;

    public string UpstreamClientId { get; set; } = string.Empty;

    /// <summary>Arrives only through the <c>Broker__UpstreamClientSecret</c> environment variable.</summary>
    public string UpstreamClientSecret { get; set; } = string.Empty;
}
