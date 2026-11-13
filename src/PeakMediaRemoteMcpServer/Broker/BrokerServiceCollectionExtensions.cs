namespace PeakMediaRemoteMcpServer.Broker;

public static class BrokerServiceCollectionExtensions
{
    /// <summary>Registers the authorization server: stores, signing key, token issuer, upstream OIDC client, janitor.</summary>
    public static IServiceCollection AddBroker(this IServiceCollection services)
    {
        services.AddSingleton<AppMetaStore>();
        services.AddSingleton<UserStore>();
        services.AddSingleton<ClientStore>();
        services.AddSingleton<TransactionStore>();
        services.AddSingleton<CodeStore>();
        services.AddSingleton<RefreshTokenStore>();
        services.AddSingleton<SigningKeyProvider>();
        services.AddSingleton<JwtIssuer>();
        services.AddHttpClient(UpstreamOidcClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<UpstreamOidcClient>();
        services.AddSingleton<BrokerJanitor>();
        services.AddHostedService(sp => sp.GetRequiredService<BrokerJanitor>());
        return services;
    }
}
