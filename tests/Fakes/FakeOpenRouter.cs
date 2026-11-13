namespace PeakMediaTestFakes;

/// <summary>
/// An in-memory stand-in for OpenRouter's speech endpoint (<c>POST /api/v1/audio/speech</c>): answers
/// with canned <c>audio/mpeg</c> bytes and records each request body. Tests never call real OpenRouter.
/// </summary>
public sealed class FakeOpenRouter : IAsyncDisposable
{
    public static readonly byte[] CannedAudio = [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    private readonly WebApplication _app;

    private FakeOpenRouter(WebApplication app)
    {
        _app = app;
    }

    /// <summary>Every request body received, in order.</summary>
    public ConcurrentQueue<string> SpeechRequests { get; } = new();

    public static async Task<FakeOpenRouter> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        var fake = new FakeOpenRouter(app);
        app.MapPost("/api/v1/audio/speech", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            fake.SpeechRequests.Enqueue(await reader.ReadToEndAsync());
            return Results.Bytes(CannedAudio, "audio/mpeg");
        });
        await app.StartAsync();
        return fake;
    }

    /// <summary>An <see cref="HttpClient"/> whose base address is the fake's <c>/api/v1/</c> root.</summary>
    public HttpClient CreateClient()
    {
        var client = _app.GetTestClient();
        client.BaseAddress = new Uri(client.BaseAddress!, "/api/v1/");
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
    }
}
