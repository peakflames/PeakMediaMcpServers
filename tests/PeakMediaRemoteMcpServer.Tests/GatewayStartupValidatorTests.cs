namespace PeakMediaRemoteMcpServer.Tests;

public class GatewayStartupValidatorTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
    }

    [Fact]
    public void Valid_configuration_passes()
    {
        var result = GatewayStartupValidator.Validate(Config(
            ("McpAuth:Enabled", "true"), ("Broker:UpstreamClientSecret", "s"), ("OpenRouter:ApiKey", "k")));

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
