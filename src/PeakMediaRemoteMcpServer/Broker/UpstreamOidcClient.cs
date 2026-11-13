namespace PeakMediaRemoteMcpServer.Broker;

/// <summary>Who signed in, taken from the validated ID token.</summary>
public sealed record UpstreamIdentity(string Subject, string Email, IReadOnlyList<string> Groups);

/// <summary>
/// The gateway's client of the organization's OIDC provider. Discovery is read lazily from
/// <c>&lt;UpstreamAuthority&gt;/.well-known/openid-configuration</c> (startup never contacts the
/// provider). The code is exchanged with <c>client_secret_basic</c> and the gateway's own PKCE
/// verifier; identity comes only from the ID token after validating its signature (provider JWKS),
/// issuer, audience (client ID), lifetime and nonce. Upstream access and refresh tokens are discarded —
/// the gateway never calls the provider on a caller's behalf.
/// </summary>
public sealed class UpstreamOidcClient
{
    public const string HttpClientName = "upstream-oidc";

    private static readonly JsonWebTokenHandler Handler = new();

    private readonly IHttpClientFactory _httpClients;
    private readonly IOptions<BrokerOptions> _options;
    private readonly ILogger<UpstreamOidcClient> _logger;
    private readonly Lazy<ConfigurationManager<OpenIdConnectConfiguration>> _configuration;

    public UpstreamOidcClient(IHttpClientFactory httpClients, IOptions<BrokerOptions> options, ILogger<UpstreamOidcClient> logger)
    {
        _httpClients = httpClients;
        _options = options;
        _logger = logger;
        _configuration = new Lazy<ConfigurationManager<OpenIdConnectConfiguration>>(CreateConfigurationManager);
    }

    public async Task<Result<OpenIdConnectConfiguration>> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Result.Ok(await _configuration.Value.GetConfigurationAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or HttpRequestException)
        {
            _logger.LogWarning("The OIDC provider's discovery document could not be read: {Problem}", ex.GetType().Name);
            return Result.Fail("the sign-in provider's discovery document could not be read");
        }
    }

    public async Task<Result<UpstreamIdentity>> ExchangeCodeAsync(string code, string codeVerifier, string expectedNonce, CancellationToken cancellationToken)
    {
        var configuration = await GetConfigurationAsync(cancellationToken);
        if (configuration.IsFailed)
        {
            return configuration.ToResult<UpstreamIdentity>();
        }

        var options = _options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, configuration.Value.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = options.CallbackUri,
                ["code_verifier"] = codeVerifier,
            }),
        };
        var basic = $"{Uri.EscapeDataString(options.UpstreamClientId)}:{Uri.EscapeDataString(options.UpstreamClientSecret)}";
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(basic)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        string? idToken;
        try
        {
            using var response = await _httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("The OIDC provider refused the code exchange with HTTP {Status}.", (int)response.StatusCode);
                return Result.Fail("the sign-in provider refused the code exchange");
            }

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            idToken = body.RootElement.TryGetProperty("id_token", out var value) ? value.GetString() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning("The OIDC provider's token endpoint could not be reached: {Problem}", ex.GetType().Name);
            return Result.Fail("the sign-in provider's token endpoint could not be reached");
        }

        if (string.IsNullOrEmpty(idToken))
        {
            return Result.Fail("the sign-in provider returned no ID token");
        }

        return await ValidateIdTokenAsync(idToken, configuration.Value, expectedNonce, refreshed: false, cancellationToken);
    }

    private async Task<Result<UpstreamIdentity>> ValidateIdTokenAsync(
        string idToken, OpenIdConnectConfiguration configuration, string expectedNonce, bool refreshed, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = configuration.Issuer,
            ValidAudience = options.UpstreamClientId,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        var validation = await Handler.ValidateTokenAsync(idToken, parameters);
        if (!validation.IsValid)
        {
            if (!refreshed && validation.Exception is SecurityTokenSignatureKeyNotFoundException)
            {
                // The provider may have rotated its keys since discovery was cached.
                _configuration.Value.RequestRefresh();
                var fresh = await GetConfigurationAsync(cancellationToken);
                if (fresh.IsSuccess)
                {
                    return await ValidateIdTokenAsync(idToken, fresh.Value, expectedNonce, refreshed: true, cancellationToken);
                }
            }

            _logger.LogWarning("The ID token from the OIDC provider failed validation: {Problem}", validation.Exception?.GetType().Name);
            return Result.Fail("the sign-in provider's ID token failed validation");
        }

        var token = (JsonWebToken)validation.SecurityToken;
        if (!token.TryGetPayloadValue<string>("nonce", out var nonce) || !string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
        {
            _logger.LogWarning("The ID token from the OIDC provider carried a wrong or missing nonce.");
            return Result.Fail("the sign-in provider's ID token carried a wrong nonce");
        }

        var subject = token.Subject;
        if (string.IsNullOrEmpty(subject))
        {
            return Result.Fail("the sign-in provider's ID token has no subject");
        }

        var email = token.Claims.FirstOrDefault(c => c.Type == "email")?.Value
            ?? token.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value
            ?? string.Empty;
        var groupsClaim = string.IsNullOrWhiteSpace(options.UpstreamGroupsClaim) ? "groups" : options.UpstreamGroupsClaim;
        var groups = token.Claims.Where(c => c.Type == groupsClaim).Select(c => c.Value).ToList();
        return Result.Ok(new UpstreamIdentity(subject, email, groups));
    }

    private ConfigurationManager<OpenIdConnectConfiguration> CreateConfigurationManager()
    {
        var authority = _options.Value.UpstreamAuthority.TrimEnd('/');
        var retriever = new HttpDocumentRetriever(_httpClients.CreateClient(HttpClientName))
        {
            RequireHttps = !(Uri.TryCreate(authority, UriKind.Absolute, out var uri) && uri.IsLoopback),
        };
        return new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{authority}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            retriever);
    }
}
