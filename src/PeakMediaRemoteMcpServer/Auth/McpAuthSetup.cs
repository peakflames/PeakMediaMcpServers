namespace PeakMediaRemoteMcpServer.Auth;

/// <summary>
/// The resource-server gate on <c>/mcp</c>. Requests authenticate with JwtBearer against the
/// gateway's own signing key; challenges go to the MCP scheme, so a 401 carries
/// <c>WWW-Authenticate: Bearer resource_metadata="G/.well-known/oauth-protected-resource"</c>, and
/// the MCP scheme serves that metadata document. Identity comes only from a validated gateway access
/// token — no header, query parameter or cookie can assert who the caller is, and there is no
/// anonymous fallback or development login.
/// </summary>
public static class McpAuthSetup
{
    public const string AdminPolicy = "admin";

    /// <summary>Dashboard pages: a signed-in dashboard session (cookie); no session → OIDC sign-in.</summary>
    public const string DashboardPolicy = "dashboard";

    public const string ProtectedResourceMetadataPath = "/.well-known/oauth-protected-resource";

    public static IServiceCollection AddCallerAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();
        services.AddSingleton<IConfigureOptions<McpAuthenticationOptions>, ConfigureMcpAuthenticationOptions>();

        services
            .AddAuthentication(options =>
            {
                // JwtBearer for /mcp and bearer requests; the dashboard cookie for the dashboard's pages.
                options.DefaultAuthenticateScheme = DashboardAuthSetup.SelectorScheme;
                options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
            })
            .AddJwtBearer()
            .AddMcp(_ => { })
            .AddDashboardAuthentication();

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => AccessRule.IsAdmin(context.User)))
            .AddPolicy(DashboardPolicy, policy => policy
                .AddAuthenticationSchemes(DashboardAuthSetup.CookieScheme)
                .RequireAuthenticatedUser());
        return services;
    }
}

/// <summary>
/// JwtBearer validation for the gateway's own tokens: issuer = <c>Broker:IssuerUri</c>, audience =
/// <c>McpAuth:Resource</c>, the in-process signing key, RS256 only, 30 s clock skew, raw claim names.
/// Named options, because JwtBearer reads its options under the scheme name.
/// </summary>
public sealed class ConfigureJwtBearerOptions(
    IOptions<BrokerOptions> brokerOptions,
    IOptions<McpAuthOptions> mcpAuthOptions,
    SigningKeyProvider signingKey) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            Configure(options);
        }
    }

    public void Configure(JwtBearerOptions options)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = brokerOptions.Value.NormalizedIssuerUri,
            ValidateAudience = true,
            ValidAudience = mcpAuthOptions.Value.Resource,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            // Resolved per request so the key is loaded from storage lazily, never at startup.
            IssuerSigningKeyResolver = (_, _, _, _) => [signingKey.Key],
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "email",
            RoleClaimType = JwtIssuer.RoleClaim,
        };
    }
}

/// <summary>
/// Protected-resource metadata (RFC 9728): resource = <c>McpAuth:Resource</c>, authorization server =
/// the gateway itself, served at exactly <c>G/.well-known/oauth-protected-resource</c>.
/// </summary>
public sealed class ConfigureMcpAuthenticationOptions(
    IOptions<BrokerOptions> brokerOptions,
    IOptions<McpAuthOptions> mcpAuthOptions) : IConfigureNamedOptions<McpAuthenticationOptions>
{
    public void Configure(string? name, McpAuthenticationOptions options)
    {
        if (string.Equals(name, McpAuthenticationDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            Configure(options);
        }
    }

    public void Configure(McpAuthenticationOptions options)
    {
        var issuer = brokerOptions.Value.NormalizedIssuerUri;
        options.ResourceMetadataUri = new Uri($"{issuer}{McpAuthSetup.ProtectedResourceMetadataPath}");
        options.ResourceMetadata = new ProtectedResourceMetadata
        {
            Resource = mcpAuthOptions.Value.Resource,
            AuthorizationServers = [issuer],
            BearerMethodsSupported = ["header"],
        };
    }
}
