namespace PeakMediaRemoteMcpServer.Tests;

public class GatewayStartupValidatorTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
    }

    private static readonly (string Key, string? Value)[] ValidSettings =
    [
        ("McpAuth:Enabled", "true"),
        ("McpAuth:Resource", "https://media.example.com/mcp"),
        ("Broker:IssuerUri", "https://media.example.com"),
        ("Broker:UpstreamAuthority", "https://example.okta.com"),
        ("Broker:UpstreamClientId", "client"),
        ("Broker:UpstreamClientSecret", "s"),
        ("OpenRouter:ApiKey", "k"),
    ];

    private static IConfiguration ValidWith(string key, string? value)
    {
        return Config([.. ValidSettings.Where(s => s.Key != key), (key, value)]);
    }

    [Fact]
    public void Valid_configuration_passes()
    {
        var result = GatewayStartupValidator.Validate(Config(ValidSettings));

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("Broker:UpstreamAuthority", "", GatewayStartupValidator.UpstreamAuthorityMissingMessage)]
    [InlineData("Broker:UpstreamAuthority", "http://example.okta.com", GatewayStartupValidator.UpstreamAuthorityInvalidMessage)]
    [InlineData("Broker:UpstreamClientId", "", GatewayStartupValidator.UpstreamClientIdMissingMessage)]
    [InlineData("Broker:IssuerUri", "not a url", GatewayStartupValidator.IssuerUriInvalidMessage)]
    [InlineData("McpAuth:Resource", "https://other.example.com/mcp", GatewayStartupValidator.ResourceMismatchMessage)]
    public void Broker_misconfiguration_is_refused(string key, string value, string expected)
    {
        var result = GatewayStartupValidator.Validate(ValidWith(key, value));

        Assert.Equal(expected, result.Errors.Single().Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Cache_limit_of_zero_or_less_is_refused(string value)
    {
        var result = GatewayStartupValidator.Validate(ValidWith("Storage:CacheMaxGb", value));

        Assert.Equal(GatewayStartupValidator.CacheMaxGbInvalidMessage, result.Errors.Single().Message);
    }

    [Fact]
    public void Fractional_cache_limit_is_allowed()
    {
        var result = GatewayStartupValidator.Validate(ValidWith("Storage:CacheMaxGb", "0.5"));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Plain_http_authority_is_allowed_on_loopback()
    {
        var result = GatewayStartupValidator.Validate(ValidWith("Broker:UpstreamAuthority", "http://127.0.0.1:8080"));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Missing_McpAuth_section_counts_as_disabled_and_is_reported_first()
    {
        var result = GatewayStartupValidator.Validate(Config());

        Assert.Equal(GatewayStartupValidator.McpAuthDisabledMessage, result.Errors.Single().Message);
    }

    [Fact]
    public void Broker_secret_is_checked_before_OpenRouter_key()
    {
        var result = GatewayStartupValidator.Validate(Config(("McpAuth:Enabled", "true")));

        Assert.Equal(GatewayStartupValidator.BrokerSecretMissingMessage, result.Errors.Single().Message);
    }

    [Fact]
    public void Whitespace_OpenRouter_key_counts_as_unset()
    {
        var result = GatewayStartupValidator.Validate(Config(
            ("McpAuth:Enabled", "true"), ("Broker:UpstreamClientSecret", "s"), ("OpenRouter:ApiKey", "  ")));

        Assert.Equal(GatewayStartupValidator.OpenRouterKeyMissingMessage, result.Errors.Single().Message);
    }
}
