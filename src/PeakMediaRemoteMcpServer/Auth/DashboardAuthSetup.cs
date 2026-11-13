namespace PeakMediaRemoteMcpServer.Auth;

/// <summary>
/// Sign-in for the usage dashboard: a cookie session (<see cref="CookieScheme"/>) started through the
/// organization's OIDC provider (<see cref="OidcScheme"/>, the same <c>Broker:Upstream*</c> client the
/// broker uses, authorization code + PKCE, callback <c>/signin-oidc</c>). The session principal carries
/// only <c>sub</c>, <c>email</c> and <c>role</c> — the same claims a gateway access token carries — so
/// <see cref="Caller.From"/> and <see cref="AccessRule"/> decide for the dashboard exactly as they do
/// for the tools. <c>/mcp</c> never reads the cookie: <see cref="SelectorScheme"/> sends it, and any
/// request with a bearer token, to JwtBearer.
/// </summary>
public static class DashboardAuthSetup
{
    public const string CookieScheme = "pm_dashboard";

    public const string OidcScheme = "pm_dashboard_oidc";

    /// <summary>The default authenticate scheme: picks JwtBearer or the dashboard cookie per request.</summary>
    public const string SelectorScheme = "pm_selector";

    public const string CallbackPath = "/signin-oidc";

    public const string SignedOutPath = "/signed-out";

    public static AuthenticationBuilder AddDashboardAuthentication(this AuthenticationBuilder builder)
    {
        builder.Services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, ConfigureDashboardOidcOptions>();

        builder
            .AddPolicyScheme(SelectorScheme, "JwtBearer for the MCP endpoint, the dashboard cookie elsewhere", options =>
            {
                options.ForwardDefaultSelector = SelectScheme;
            })
            .AddCookie(CookieScheme, options =>
            {
                // Session and sign-in cookies follow the wall clock, whatever clock usage figures use.
                options.TimeProvider = TimeProvider.System;
                options.Cookie.Name = CookieScheme;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.ForwardChallenge = OidcScheme;
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(OidcScheme, _ => { });
        return builder;
    }

    /// <summary>JwtBearer for <c>/mcp</c> and for any request presenting a bearer token; otherwise the dashboard cookie.</summary>
    public static string SelectScheme(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (context.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase)
            || authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return JwtBearerDefaults.AuthenticationScheme;
        }

        return CookieScheme;
    }

    /// <summary>The session principal: <c>sub</c>, <c>email</c> and <c>role</c> only (no tokens, no groups).</summary>
    public static ClaimsPrincipal SessionPrincipal(string subject, string email, bool isAdmin)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim("sub", subject),
                new Claim("email", email),
                new Claim(JwtIssuer.RoleClaim, isAdmin ? JwtIssuer.AdminRole : JwtIssuer.UserRole),
            ],
            CookieScheme,
            "email",
            JwtIssuer.RoleClaim);
        return new ClaimsPrincipal(identity);
    }
}

/// <summary>
/// The dashboard's OIDC client: the broker's upstream provider settings, code flow with PKCE, ID token
/// only (nothing saved), the admin role from the configured groups claim (<see cref="AdminGroup"/>), and
/// the person recorded in <c>users</c> on every sign-in, as the broker does.
/// </summary>
public sealed class ConfigureDashboardOidcOptions(
    IOptions<BrokerOptions> brokerOptions,
    IOptions<AdminOptions> adminOptions) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (string.Equals(name, DashboardAuthSetup.OidcScheme, StringComparison.Ordinal))
        {
            Configure(options);
        }
    }

    public void Configure(OpenIdConnectOptions options)
    {
        var broker = brokerOptions.Value;
        options.SignInScheme = DashboardAuthSetup.CookieScheme;
        options.TimeProvider = TimeProvider.System;
        options.Authority = broker.UpstreamAuthority.TrimEnd('/');
        options.RequireHttpsMetadata = broker.UpstreamAuthority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        options.ClientId = broker.UpstreamClientId;
        options.ClientSecret = broker.UpstreamClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        // The code comes back on a top-level GET, which carries the Lax correlation and nonce cookies;
        // form_post would be a cross-site POST that drops them.
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.UsePkce = true;
        options.CallbackPath = DashboardAuthSetup.CallbackPath;
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.MapInboundClaims = false;
        options.Scope.Clear();
        foreach (var scope in broker.UpstreamScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            options.Scope.Add(scope);
        }

        // The provider redirects back with a top-level GET, so Lax cookies arrive; Secure only on HTTPS.
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.TokenValidationParameters.NameClaimType = "email";

        var groupsClaim = broker.UpstreamGroupsClaim;
        var adminGroup = adminOptions.Value.Group;
        options.Events.OnTokenValidated = async context =>
        {
            var principal = context.Principal;
            var subject = principal?.FindFirst("sub")?.Value;
            if (principal is null || string.IsNullOrWhiteSpace(subject))
            {
                context.Fail("the ID token carries no subject");
                return;
            }

            var email = principal.FindFirst("email")?.Value ?? string.Empty;
            var groups = principal.FindAll(groupsClaim).Select(c => c.Value);
            var isAdmin = AdminGroup.Grants(groups, adminGroup);
            var users = context.HttpContext.RequestServices.GetRequiredService<UserStore>();
            var recorded = await users.UpsertSignInAsync(subject, email, isAdmin, DateTimeOffset.UtcNow, context.HttpContext.RequestAborted);
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PeakMediaRemoteMcpServer.Dashboard");
            if (recorded.IsFailed)
            {
                logger.LogError("A dashboard sign-in could not be recorded: {Problem}", recorded.Errors[0].Message);
                context.Fail("the sign-in could not be recorded");
                return;
            }

            logger.LogInformation("Dashboard sign-in completed with role {Role}.", isAdmin ? JwtIssuer.AdminRole : JwtIssuer.UserRole);
            context.Principal = DashboardAuthSetup.SessionPrincipal(subject, email, isAdmin);
        };
        options.Events.OnRemoteFailure = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PeakMediaRemoteMcpServer.Dashboard");
            logger.LogWarning("A dashboard sign-in failed at the provider or callback.");
            context.Response.Redirect(DashboardAuthSetup.SignedOutPath);
            context.HandleResponse();
            return Task.CompletedTask;
        };
    }
}
