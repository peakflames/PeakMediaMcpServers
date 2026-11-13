namespace PeakMediaTestSupport;

/// <summary>A tool result from the stdio server, with the progress notifications sent before it.</summary>
public sealed record StdioToolCall(string Text, IReadOnlyList<JsonElement> Progress, bool ProgressBeforeResult, string RawResponse);

/// <summary>
/// Drives the built stdio server (<c>PeakMediaMcpServer</c>) as a real child process over stdin/stdout:
/// initialize, <c>tools/list</c> and <c>tools/call</c> (optionally with a progress token). Each client
/// gets its own home directory (<c>HOME</c>) so the default cache <c>~/.cache/peak-media/tts</c> is fresh,
/// and points the server's OpenRouter traffic at a fake with fast retry settings.
/// </summary>
public sealed class StdioClient : IDisposable
{
    public const string Project = "PeakMediaMcpServer";
    public const string DefaultKey = "sk-or-test-key";
    public const string ProgressToken = "progress-1";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private int _nextId = 10;

    private StdioClient(ServerProcess process, TempDirectory home)
    {
        Process = process;
        Home = home;
    }

    public ServerProcess Process { get; }

    /// <summary>The server's home directory H (a fresh temp directory).</summary>
    public TempDirectory Home { get; }

    /// <summary>The default local cache, <c>H/.cache/peak-media/tts</c>.</summary>
    public string DefaultCacheDirectory => System.IO.Path.Combine(Home.Path, ".cache", "peak-media", "tts");

    /// <summary>
    /// Starts the server with <c>PEAK_MEDIA_OPENROUTER_API_KEY</c> = <see cref="DefaultKey"/> unless
    /// <paramref name="environment"/> sets or removes it (null removes), and completes the handshake.
    /// </summary>
    public static async Task<StdioClient> StartAsync(string openRouterBaseUrl, IDictionary<string, string?>? environment = null)
    {
        var home = new TempDirectory();
        var settings = new Dictionary<string, string?>
        {
            ["HOME"] = home.Path,
            ["PEAK_MEDIA_OPENROUTER_API_KEY"] = DefaultKey,
            ["OpenRouter__BaseUrl"] = openRouterBaseUrl,
            ["OpenRouter__RetryDelayMs"] = "10",
            ["OpenRouter__ClipTimeout"] = "00:00:10",
            ["OpenRouter__CostLookupDelaysMs__0"] = "1",
            ["OpenRouter__CostLookupDelaysMs__1"] = "2",
            ["OpenRouter__CostLookupDelaysMs__2"] = "4",
        };
        foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
        {
            settings[name] = value;
        }

        var process = ServerProcess.Start(Project, [], settings);
        var client = new StdioClient(process, home);
        await client.InitializeAsync();
        return client;
    }

    public async Task<IReadOnlyList<JsonElement>> ListToolsAsync()
    {
        var response = await SendAsync("tools/list", new Dictionary<string, object>());
        return [.. response.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.Clone())];
    }

    public Task<StdioToolCall> SynthesizeAsync(object[] clips, string? model = null, string? voice = null, string? style = null, bool withProgress = false)
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

        return CallToolAsync("synthesize_narration", arguments, withProgress);
    }

    public async Task<StdioToolCall> CallToolAsync(string name, object arguments, bool withProgress = false)
    {
        var parameters = new Dictionary<string, object> { ["name"] = name, ["arguments"] = arguments };
        if (withProgress)
        {
            parameters["_meta"] = new Dictionary<string, object> { ["progressToken"] = ProgressToken };
        }

        var before = Process.StdoutLines.Count;
        var (response, raw) = await SendRawAsync("tools/call", parameters);
        var lines = Process.StdoutLines.Skip(before).ToList();
        var resultIndex = lines.IndexOf(raw);
        var progress = lines
            .Select((line, index) => (line, index))
            .Where(x => IsProgress(x.line))
            .ToList();
        var content = response.GetProperty("result").GetProperty("content").EnumerateArray().ToList();
        var text = string.Concat(content.Where(c => c.GetProperty("type").GetString() == "text").Select(c => c.GetProperty("text").GetString()));
        return new StdioToolCall(
            text,
            [.. progress.Select(x => JsonDocument.Parse(x.line).RootElement.GetProperty("params").Clone())],
            progress.All(x => x.index < resultIndex),
            raw);
    }

    /// <summary>Clips n=1..count with distinct, ordinary sentences.</summary>
    public static object[] Clips(int count, string prefix = "Clip")
    {
        return [.. Enumerable.Range(1, count).Select(n => Clip(n, TextOf(n, prefix)))];
    }

    public static string TextOf(int n, string prefix = "Clip")
    {
        return $"{prefix} {n} says hello to everyone listening today.";
    }

    public static object Clip(int n, string text)
    {
        return new Dictionary<string, object> { ["n"] = n, ["text"] = text };
    }

    /// <summary>The clip entries of the result's single fenced JSON block.</summary>
    public static IReadOnlyList<JsonElement> Entries(string text)
    {
        var blocks = Regex.Matches(text, "```json\\n(.*?)\\n```", RegexOptions.Singleline).Select(m => m.Groups[1].Value).ToList();
        if (blocks.Count != 1)
        {
            throw new InvalidOperationException($"expected exactly one JSON block in the result:\n{text}");
        }

        return [.. JsonDocument.Parse(blocks[0]).RootElement.EnumerateArray().Select(e => e.Clone())];
    }

    /// <summary>True when <paramref name="path"/> holds a complete 16-bit mono 24 kHz PCM WAV.</summary>
    public static bool IsValidWav(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var wav = File.ReadAllBytes(path);
        return wav.Length >= 44
            && Encoding.ASCII.GetString(wav, 0, 4) == "RIFF"
            && Encoding.ASCII.GetString(wav, 8, 4) == "WAVE"
            && Encoding.ASCII.GetString(wav, 12, 4) == "fmt "
            && BitConverter.ToInt16(wav, 20) == 1
            && BitConverter.ToInt16(wav, 22) == 1
            && BitConverter.ToInt32(wav, 24) == 24000
            && BitConverter.ToInt16(wav, 34) == 16
            && Encoding.ASCII.GetString(wav, 36, 4) == "data"
            && BitConverter.ToInt32(wav, 40) == wav.Length - 44;
    }

    /// <summary>Closes stdin so the server shuts down cleanly, and waits for it.</summary>
    public async Task StopAsync()
    {
        Process.CloseStdin();
        await Process.WaitForExitAsync(Timeout);
    }

    public void Dispose()
    {
        Process.Dispose();
        Home.Dispose();
    }

    private async Task InitializeAsync()
    {
        await SendAsync("initialize", new Dictionary<string, object>
        {
            ["protocolVersion"] = "2025-06-18",
            ["capabilities"] = new Dictionary<string, object>(),
            ["clientInfo"] = new Dictionary<string, object> { ["name"] = "peakmedia-tests", ["version"] = "1.0.0" },
        });
        await Process.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
    }

    private async Task<JsonElement> SendAsync(string method, object parameters)
    {
        return (await SendRawAsync(method, parameters)).Response;
    }

    private async Task<(JsonElement Response, string Raw)> SendRawAsync(string method, object parameters)
    {
        var id = Interlocked.Increment(ref _nextId);
        var request = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters,
        });
        await Process.WriteLineAsync(request);
        var raw = await Process.WaitForStdoutAsync(line => IsResponse(line, id), Timeout);
        return (JsonDocument.Parse(raw).RootElement.Clone(), raw);
    }

    private static bool IsResponse(string line, int id)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.TryGetProperty("id", out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.GetInt32() == id;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsProgress(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.TryGetProperty("method", out var method) && method.GetString() == "notifications/progress";
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
