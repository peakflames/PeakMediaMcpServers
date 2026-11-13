namespace PeakMediaRemoteMcpServer.Tests.Usage;

/// <summary>
/// The built gateway as a real child process (stub OIDC issuer and fake OpenRouter on real ports), for
/// requirements about what the gateway writes to its database file, standard error and <c>logs/</c>.
/// </summary>
[Collection("GatewayProcess")]
public class UsageProcessTests
{
    private const string Marker = "Zebra-quartz-lantern-7391";

    // TOR-05-ryNGNXA: a clip whose text is "Zebra-quartz-lantern-7391 is the marker sentence." is
    // synthesized; the marker is in neither the SQLite database file (nor its -wal/-shm), standard error,
    // nor any file under logs/.
    [Fact]
    public async Task TOR_05_ryNGNXA_narration_text_is_never_stored_or_logged()
    {
        await using var openRouter = await FakeOpenRouter.StartListeningAsync();
        using var data = new TempDirectory();

        var (text, process) = await RunOneNarrationAsync(openRouter, data, $"{Marker} is the marker sentence.");

        using (process)
        {
            Assert.False(text.StartsWith("ERROR:", StringComparison.Ordinal), text);
            Assert.Equal($"{Marker} is the marker sentence.", Assert.Single(openRouter.Requests).Input);
            var databaseFiles = Directory.GetFiles(data.Path, "peakmedia.db*");
            Assert.Contains(databaseFiles, f => Path.GetFileName(f) == "peakmedia.db");
            foreach (var file in databaseFiles)
            {
                var bytes = await ReadAllBytesSharedAsync(file);
                Assert.False(Contains(bytes, Encoding.UTF8.GetBytes(Marker)), $"{Path.GetFileName(file)} contains the narration text");
                Assert.False(Contains(bytes, Encoding.Unicode.GetBytes(Marker)), $"{Path.GetFileName(file)} contains the narration text (UTF-16)");
            }

            Assert.DoesNotContain(Marker, process.Stderr, StringComparison.Ordinal);
            var logDirectory = RepoPaths.LogDirectory(GatewayProcess.Project);
            var logFiles = Directory.GetFiles(logDirectory, "*", SearchOption.AllDirectories);
            Assert.NotEmpty(logFiles);
            foreach (var file in logFiles)
            {
                Assert.DoesNotContain(Marker, string.Join('\n', GatewayStartupLogTests.ReadLinesShared(file)), StringComparison.Ordinal);
            }
        }
    }

    // TOR-05-dFazrDm (log half; ChargingTests checks the charged amount): every generation-statistics
    // request fails, and the gateway's log holds a [WRN] record saying the cost was estimated from the
    // configured price.
    [Fact]
    public async Task TOR_05_dFazrDm_estimated_cost_is_logged_as_a_warning()
    {
        await using var openRouter = await FakeOpenRouter.StartListeningAsync();
        openRouter.GenerationNotReadyFor = int.MaxValue;
        using var data = new TempDirectory();

        var (text, process) = await RunOneNarrationAsync(openRouter, data, string.Concat(Enumerable.Repeat("Word ", 200)));

        using (process)
        {
            Assert.False(text.StartsWith("ERROR:", StringComparison.Ordinal), text);
            Assert.Contains(
                process.StderrLines,
                line => line.Contains("[WRN]", StringComparison.Ordinal)
                    && line.Contains("estimated from the configured price", StringComparison.Ordinal));
        }
    }

    private static async Task<(string Text, ServerProcess Process)> RunOneNarrationAsync(FakeOpenRouter openRouter, TempDirectory data, string clipText)
    {
        await using var stub = await StubOidcIssuer.StartAsync();
        var port = FreePort.Next();
        var environment = GatewayProcess.ValidEnvironment(port, data);
        environment["Broker__UpstreamAuthority"] = stub.Authority;
        environment["Broker__UpstreamClientId"] = stub.ClientId;
        environment["Broker__UpstreamClientSecret"] = stub.ClientSecret;
        environment["OpenRouter__BaseUrl"] = openRouter.BaseUrl;
        environment["OpenRouter__RetryDelayMs"] = "1";
        environment["OpenRouter__CostLookupDelaysMs__0"] = "1";
        environment["OpenRouter__CostLookupDelaysMs__1"] = "2";
        var process = GatewayProcess.Start(environment);
        try
        {
            using var gateway = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(30),
            };
            await GatewayProcess.WaitUntilReadyAsync(process, gateway);
            var signedIn = await TestSignIn.SignInAsync(gateway, stub, StubUser.For("pat@example.com"));

            var text = await CallSynthesizeAsync(gateway, signedIn.AccessToken, clipText);
            await process.StopAsync();
            return (text, process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private static async Task<string> CallSynthesizeAsync(HttpClient gateway, string accessToken, string clipText)
    {
        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 7,
            ["method"] = "tools/call",
            ["params"] = new Dictionary<string, object>
            {
                ["name"] = "synthesize_narration",
                ["arguments"] = new Dictionary<string, object> { ["clips"] = new[] { NarrationClient.Clip(1, clipText) } },
            },
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await gateway.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await TestSignIn.ReadJsonRpcAsync(response);
        return string.Concat(document.RootElement.GetProperty("result").GetProperty("content").EnumerateArray()
            .Where(c => c.GetProperty("type").GetString() == "text")
            .Select(c => c.GetProperty("text").GetString()));
    }

    private static async Task<byte[]> ReadAllBytesSharedAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        return haystack.AsSpan().IndexOf(needle) >= 0;
    }
}
