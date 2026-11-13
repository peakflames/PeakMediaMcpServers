namespace PeakMediaRemoteMcpServer.Tests.Narration;

/// <summary>An MCP tool result as the caller received it, with any progress notifications sent before it.</summary>
public sealed record ToolCall(
    string Text,
    IReadOnlyList<string> ContentTypes,
    IReadOnlyList<JsonElement> Progress,
    bool ProgressBeforeResult,
    string RawBody);

/// <summary>
/// A gateway with the fake OpenRouter, the stub OIDC issuer and a controllable clock, plus a caller
/// signed in through the real broker flow (<see cref="TestSignIn"/>) — no auth bypass. Calls tools
/// with JSON-RPC <c>tools/call</c> over the Streamable HTTP transport. <see cref="StartLiveAsync"/>
/// talks to the real OpenRouter instead of the fake.
/// </summary>
public sealed class NarrationClient : IAsyncDisposable
{
    public const string ProgressToken = "progress-1";

    private readonly FakeOpenRouter? _openRouter;
    private int _nextId = 10;

    private NarrationClient(StubOidcIssuer stub, FakeOpenRouter? openRouter, GatewayTestHost host, FakeTimeProvider clock, string accessToken)
    {
        Stub = stub;
        _openRouter = openRouter;
        Host = host;
        Clock = clock;
        AccessToken = accessToken;
    }

    public StubOidcIssuer Stub { get; }

    public FakeOpenRouter OpenRouter => _openRouter ?? throw new InvalidOperationException("A live client has no fake OpenRouter.");

    public GatewayTestHost Host { get; }

    public FakeTimeProvider Clock { get; }

    public string AccessToken { get; }

    public static async Task<NarrationClient> StartAsync(string email = "pat@example.com")
    {
        var stub = await StubOidcIssuer.StartAsync();
        var openRouter = await FakeOpenRouter.StartAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = await GatewayTestHost.StartAsync(stub, openRouter, services => services.AddSingleton<TimeProvider>(clock));
        var signedIn = await TestSignIn.SignInAsync(host.Client, stub, StubUser.For(email));
        return new NarrationClient(stub, openRouter, host, clock, signedIn.AccessToken);
    }

    /// <summary>A gateway that calls the real OpenRouter with <paramref name="openRouterApiKey"/>; sign-in still uses the stub issuer.</summary>
    public static async Task<NarrationClient> StartLiveAsync(string openRouterApiKey, string email = "pat@example.com")
    {
        var stub = await StubOidcIssuer.StartAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = await GatewayTestHost.StartLiveAsync(stub, openRouterApiKey, services => services.AddSingleton<TimeProvider>(clock));
        var signedIn = await TestSignIn.SignInAsync(host.Client, stub, StubUser.For(email));
        return new NarrationClient(stub, null, host, clock, signedIn.AccessToken);
    }

    /// <summary>Signs a second caller in to the same gateway and returns their access token.</summary>
    public async Task<string> SignInAnotherAsync(string email)
    {
        var signedIn = await TestSignIn.SignInAsync(Host.Client, Stub, StubUser.For(email));
        return signedIn.AccessToken;
    }

    public static object Clip(int n, string text, string? style = null, string? voice = null)
    {
        var clip = new Dictionary<string, object> { ["n"] = n, ["text"] = text };
        if (style is not null)
        {
            clip["style"] = style;
        }

        if (voice is not null)
        {
            clip["voice"] = voice;
        }

        return clip;
    }

    /// <summary>Clips n=1..count with distinct, ordinary sentences.</summary>
    public static object[] Clips(int count, string prefix = "Clip")
    {
        return [.. Enumerable.Range(1, count).Select(n => Clip(n, $"{prefix} {n} says hello to everyone listening today."))];
    }

    public Task<ToolCall> SynthesizeAsync(object[] clips, string? model = null, string? voice = null, string? style = null, bool withProgress = false, string? accessToken = null)
    {
        var arguments = new Dictionary<string, object> { ["clips"] = clips };
        if (model is not null)
        {
            arguments["model"] = model;
        }

        if (voice is not null)
        {
            arguments["voice"] = voice;
        }

        if (style is not null)
        {
            arguments["style"] = style;
        }

        return CallToolAsync("synthesize_narration", arguments, withProgress, accessToken);
    }

    public async Task<ToolCall> CallToolAsync(string name, object arguments, bool withProgress = false, string? accessToken = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var parameters = new Dictionary<string, object> { ["name"] = name, ["arguments"] = arguments };
        if (withProgress)
        {
            parameters["_meta"] = new Dictionary<string, object> { ["progressToken"] = ProgressToken };
        }

        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = "tools/call",
            ["params"] = parameters,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken ?? AccessToken);
        using var response = await Host.Client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"tools/call answered {(int)response.StatusCode}: {raw}");

        var messages = raw.Split('\n')
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line["data:".Length..].Trim()).RootElement.Clone())
            .ToList();
        if (messages.Count == 0)
        {
            messages.Add(JsonDocument.Parse(raw).RootElement.Clone());
        }

        var resultIndex = messages.FindIndex(m => m.TryGetProperty("id", out var mid) && mid.ValueKind == JsonValueKind.Number && mid.GetInt32() == id);
        Assert.True(resultIndex >= 0, $"no JSON-RPC response with id {id}: {raw}");
        var progressIndexes = messages
            .Select((m, i) => (m, i))
            .Where(x => x.m.TryGetProperty("method", out var method) && method.GetString() == "notifications/progress")
            .ToList();
        var result = messages[resultIndex].GetProperty("result");
        var content = result.GetProperty("content").EnumerateArray().ToList();
        var text = string.Concat(content.Where(c => c.GetProperty("type").GetString() == "text").Select(c => c.GetProperty("text").GetString()));
        return new ToolCall(
            text,
            [.. content.Select(c => c.GetProperty("type").GetString()!)],
            [.. progressIndexes.Select(x => x.m.GetProperty("params"))],
            progressIndexes.All(x => x.i < resultIndex),
            raw);
    }

    /// <summary>The tool names from a signed-in <c>tools/list</c>.</summary>
    public async Task<IReadOnlyList<string>> ListToolNamesAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        using var response = await Host.Client.SendAsync(request);
        using var document = await TestSignIn.ReadJsonRpcAsync(response);
        return [.. document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()!)];
    }

    /// <summary>The clip entries of the result's single fenced JSON block.</summary>
    public static IReadOnlyList<JsonElement> Entries(string text)
    {
        var blocks = JsonBlocks(text);
        Assert.Single(blocks);
        return [.. JsonDocument.Parse(blocks[0]).RootElement.EnumerateArray().Select(e => e.Clone())];
    }

    public static IReadOnlyList<string> JsonBlocks(string text)
    {
        return [.. Regex.Matches(text, "```json\\n(.*?)\\n```", RegexOptions.Singleline).Select(m => m.Groups[1].Value)];
    }

    /// <summary>GET a download URL with no Authorization header.</summary>
    public async Task<HttpResponseMessage> DownloadAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await Host.Client.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        if (_openRouter is not null)
        {
            await _openRouter.DisposeAsync();
        }

        await Stub.DisposeAsync();
    }
}
