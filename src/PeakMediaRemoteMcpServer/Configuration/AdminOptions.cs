namespace PeakMediaRemoteMcpServer.Configuration;

/// <summary>The <c>Admin</c> section: who holds the <c>admin</c> role.</summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// The OIDC group name that grants the <c>admin</c> role when the ID token's <c>groups</c> claim
    /// contains it. Empty means nobody is an administrator.
    /// </summary>
    public string Group { get; set; } = string.Empty;
}
