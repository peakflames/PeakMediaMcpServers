namespace PeakMediaTestFakes;

/// <summary>One request the fake received at <c>POST /api/v1/audio/speech</c>, parsed.</summary>
public sealed class FakeSpeechRequest
{
    public required string Body { get; init; }

    public required JsonElement Json { get; init; }

    public string? Authorization { get; init; }

    public string? Model => Text("model");

    public string? Voice => Text("voice");

    public string? Input => Text("input");

    public string? ResponseFormat => Text("response_format");

    public bool HasInstructions => Json.TryGetProperty("instructions", out _);

    public string? Instructions => Text("instructions");

    public bool? Zdr => Json.TryGetProperty("provider", out var provider)
        && provider.TryGetProperty("zdr", out var zdr)
        && zdr.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? zdr.GetBoolean()
            : null;

    private string? Text(string key)
    {
        return Json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}

/// <summary>
/// An in-memory stand-in for OpenRouter (<c>POST /api/v1/audio/speech</c> and
/// <c>GET /api/v1/generation</c>). It answers with silent 24 kHz 16-bit mono PCM whose length makes
/// the clip's words play at <see cref="WpmFor"/> words per minute, reports <see cref="CostFor"/> as the
/// generation's total cost, and records every speech request. Tests never call real OpenRouter.
/// </summary>
public sealed partial class FakeOpenRouter : IAsyncDisposable
{
    public const string GenerationIdHeader = "X-Generation-Id";

    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, decimal> _costs = new();
    private readonly ConcurrentQueue<FakeSpeechRequest> _requests = new();

    private FakeOpenRouter(WebApplication app)
    {
        _app = app;
    }

    /// <summary>Words per minute of the audio returned for a clip's text (default 150).</summary>
    public Func<string, double> WpmFor { get; set; } = _ => 150;

    /// <summary>The generation cost reported for a clip's text (default $0.0010).</summary>
    public Func<string, decimal> CostFor { get; set; } = _ => 0.0010m;

    /// <summary>When it returns a status code, the speech request fails with it instead of answering audio.</summary>
    public Func<FakeSpeechRequest, int?> StatusOverride { get; set; } = _ => null;

    /// <summary>Every speech request received, in order.</summary>
    public IReadOnlyList<FakeSpeechRequest> Requests => [.. _requests];

    public static async Task<FakeOpenRouter> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        var fake = new FakeOpenRouter(app);
        app.MapPost("/api/v1/audio/speech", fake.SpeechAsync);
        app.MapGet("/api/v1/generation", (string id) => fake._costs.TryGetValue(id, out var cost)
            ? Results.Json(new { data = new { id, total_cost = cost } })
            : Results.NotFound());
        await app.StartAsync();
        return fake;
    }

    /// <summary>Counts spoken words the way the gateway does: mood tags such as &lt;Warm&gt; are not words.</summary>
    public static int CountWords(string text)
    {
        return MoodTag().Replace(text, " ")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Count(token => token.Any(char.IsLetterOrDigit));
    }

    /// <summary>A handler that routes an <see cref="HttpClient"/> to the fake.</summary>
    public HttpMessageHandler CreateHandler()
    {
        return _app.GetTestServer().CreateHandler();
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

    private async Task<IResult> SpeechAsync(HttpRequest request, HttpResponse response)
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        using var document = JsonDocument.Parse(body);
        var received = new FakeSpeechRequest
        {
            Body = body,
            Json = document.RootElement.Clone(),
            Authorization = request.Headers.Authorization.ToString(),
        };
        _requests.Enqueue(received);

        if (StatusOverride(received) is { } status)
        {
            return Results.StatusCode(status);
        }

        var text = received.Input ?? string.Empty;
        var words = Math.Max(1, CountWords(text));
        var durationMs = words * 60000.0 / WpmFor(text);
        var bytes = (int)Math.Round(durationMs * 48 / 2) * 2;
        var id = $"gen-{Guid.NewGuid():N}";
        _costs[id] = CostFor(text);
        response.Headers[GenerationIdHeader] = id;
        return Results.Bytes(new byte[bytes], "application/octet-stream");
    }

    [GeneratedRegex(@"<[^<>]*>")]
    private static partial Regex MoodTag();
}
