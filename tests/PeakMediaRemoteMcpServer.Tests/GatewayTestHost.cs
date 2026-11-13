namespace PeakMediaRemoteMcpServer.Tests;

/// <summary>
/// The gateway in-process: built through <see cref="Program.BuildApp"/> on a test server with
/// in-memory configuration and a fresh temp database per host. Its public URL G is
/// <c>http://localhost</c> (the test server's base address).
/// </summary>
public sealed class GatewayTestHost : IAsyncDisposable
{
    public const string BaseUrl = "http://localhost";

    private readonly TempDirectory _data;
    private readonly WebApplication _app;

    private GatewayTestHost(WebApplication app, TempDirectory data)
    {
        _app = app;
        _data = data;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => _app.Services;

    /// <summary>A host whose sign-in provider is unreachable — for tests that never sign in.</summary>
    public static Task<GatewayTestHost> StartAsync(IDictionary<string, string?>? overrides = null)
    {
        return StartAsync("http://127.0.0.1:9", "unreachable-client", "test-secret", overrides);
    }

    /// <summary>A host that delegates sign-in to the stub OIDC issuer.</summary>
    public static Task<GatewayTestHost> StartAsync(StubOidcIssuer stub, IDictionary<string, string?>? overrides = null)
    {
        return StartAsync(stub.Authority, stub.ClientId, stub.ClientSecret, overrides);
    }

    /// <summary>
    /// A host that delegates sign-in to the stub OIDC issuer, sends OpenRouter traffic to
    /// <paramref name="openRouter"/>, and lets a test replace services (e.g. the <see cref="TimeProvider"/>).
    /// </summary>
    public static Task<GatewayTestHost> StartAsync(
        StubOidcIssuer stub,
        FakeOpenRouter openRouter,
        Action<IServiceCollection>? configureServices = null,
        IDictionary<string, string?>? overrides = null)
    {
        return StartAsync(stub.Authority, stub.ClientId, stub.ClientSecret, overrides, openRouter, configureServices);
    }

    /// <summary>
    /// A host that delegates sign-in to the stub OIDC issuer and sends OpenRouter traffic to the real
    /// OpenRouter with <paramref name="openRouterApiKey"/> — for the opt-in live tests only.
    /// </summary>
    public static Task<GatewayTestHost> StartLiveAsync(
        StubOidcIssuer stub,
        string openRouterApiKey,
        Action<IServiceCollection>? configureServices = null)
    {
        var overrides = new Dictionary<string, string?>
        {
            ["OpenRouter:ApiKey"] = openRouterApiKey,
            ["OpenRouter:BaseUrl"] = "https://openrouter.ai/api/v1",
        };
        return StartAsync(stub.Authority, stub.ClientId, stub.ClientSecret, overrides, null, configureServices);
    }

    /// <summary>Counts rows in a gateway table (table names are test constants).</summary>
    public async Task<long> CountRowsAsync(string table, string? subject = null)
    {
        var opened = await Services.GetRequiredService<SqliteConnectionFactory>().OpenAsync();
        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = subject is null
            ? $"SELECT COUNT(*) FROM {table};"
            : $"SELECT COUNT(*) FROM {table} WHERE subject = $subject;";
        if (subject is not null)
        {
            command.Parameters.AddWithValue("$subject", subject);
        }

        return (long)(await command.ExecuteScalarAsync())!;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
        _data.Dispose();
    }

    private static async Task<GatewayTestHost> StartAsync(
        string authority,
        string clientId,
        string clientSecret,
        IDictionary<string, string?>? overrides,
        FakeOpenRouter? openRouter = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var data = new TempDirectory();
        var builder = Program.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        var settings = new Dictionary<string, string?>
        {
            ["McpAuth:Enabled"] = "true",
            ["McpAuth:Resource"] = $"{BaseUrl}/mcp",
            ["Broker:IssuerUri"] = BaseUrl,
            ["Broker:UpstreamAuthority"] = authority,
            ["Broker:UpstreamClientId"] = clientId,
            ["Broker:UpstreamClientSecret"] = clientSecret,
            ["Admin:Group"] = TestSignIn.AdminGroup,
            ["OpenRouter:ApiKey"] = "test-key",
            ["Storage:DatabasePath"] = data.File("peakmedia.db"),
        };
        if (openRouter is not null)
        {
            // The fake publishes a generation's cost as soon as it is told to; retry in milliseconds.
            settings["OpenRouter:CostLookupDelaysMs:0"] = "1";
            settings["OpenRouter:CostLookupDelaysMs:1"] = "2";
            settings["OpenRouter:CostLookupDelaysMs:2"] = "4";
        }

        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        if (openRouter is not null)
        {
            settings["OpenRouter:BaseUrl"] = "http://openrouter.test/api/v1";
            builder.Services.AddHttpClient(Program.OpenRouterHttpClientName)
                .ConfigurePrimaryHttpMessageHandler(openRouter.CreateHandler);
        }

        builder.Configuration.AddInMemoryCollection(settings);
        configureServices?.Invoke(builder.Services);
        var app = Program.BuildApp(builder);
        await app.StartAsync();
        return new GatewayTestHost(app, data);
    }
}
