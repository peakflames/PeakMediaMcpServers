namespace PeakMediaRemoteMcpServer.Tests;

/// <summary>
/// The gateway in-process: built through <see cref="Program.BuildApp"/> on a test server with
/// in-memory configuration and a fresh temp database per host.
/// </summary>
public sealed class GatewayTestHost : IAsyncDisposable
{
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

    public static async Task<GatewayTestHost> StartAsync(IDictionary<string, string?>? overrides = null)
    {
        var data = new TempDirectory();
        var builder = Program.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        var settings = new Dictionary<string, string?>
        {
            ["McpAuth:Enabled"] = "true",
            ["McpAuth:Resource"] = "http://localhost/mcp",
            ["Broker:UpstreamClientSecret"] = "test-secret",
            ["OpenRouter:ApiKey"] = "test-key",
            ["Storage:DatabasePath"] = data.File("peakmedia.db"),
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);
        var app = Program.BuildApp(builder);
        await app.StartAsync();
        return new GatewayTestHost(app, data);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
        _data.Dispose();
    }
}
