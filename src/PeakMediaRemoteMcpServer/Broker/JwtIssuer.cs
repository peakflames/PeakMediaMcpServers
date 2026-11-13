namespace PeakMediaRemoteMcpServer.Broker;

/// <summary>
/// Mints the gateway's RS256 access tokens: <c>iss</c> = <c>Broker:IssuerUri</c>,
/// <c>aud</c> = <c>McpAuth:Resource</c>, <c>sub</c> (OIDC subject), <c>email</c>, <c>role</c>
/// (<c>admin</c> | <c>user</c>), <c>jti</c>; lifetime <c>Broker:IssuedAccessTokenLifetimeMinutes</c>.
/// </summary>
public sealed class JwtIssuer(SigningKeyProvider signingKey, IOptions<BrokerOptions> brokerOptions, IOptions<McpAuthOptions> mcpAuthOptions)
{
    public const string RoleClaim = "role";
    public const string AdminRole = "admin";
    public const string UserRole = "user";

    private static readonly JsonWebTokenHandler Handler = new();

    public int LifetimeSeconds => brokerOptions.Value.IssuedAccessTokenLifetimeMinutes * 60;

    public string IssueAccessToken(string subject, string email, bool isAdmin, DateTimeOffset now)
    {
        var broker = brokerOptions.Value;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = broker.NormalizedIssuerUri,
            Audience = mcpAuthOptions.Value.Resource,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(broker.IssuedAccessTokenLifetimeMinutes).UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["email"] = email,
                [RoleClaim] = isAdmin ? AdminRole : UserRole,
                ["jti"] = Guid.NewGuid().ToString("N"),
            },
            SigningCredentials = new SigningCredentials(signingKey.Key, SecurityAlgorithms.RsaSha256),
        };

        return Handler.CreateToken(descriptor);
    }
}
