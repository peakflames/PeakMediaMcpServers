namespace PeakMediaRemoteMcpServer.Tests;

/// <summary>
/// The built gateway as a real child process, with the TOR's literal OpenRouter key and OIDC client
/// secret, the stub OIDC issuer and the fake OpenRouter on real ports: neither secret may appear on
/// standard error or in any file under <c>logs/</c>.
/// </summary>
[Collection("GatewayProcess")]
public class SecretsInLogTests
{
    private const string OpenRouterKey = "sk-or-test-SECRET-KEY-123";
    private const string ClientSecret = "oidc-test-SECRET-456";

    // TOR-01-x5DXvRT: start, sign in, one successful synthesize_narration call, stop; stderr and logs/ hold neither secret.
    [Fact]
    public async Task TOR_01_x5DXvRT_secrets_are_not_in_stderr_or_log_files()
    {
        await using var openRouter = await FakeOpenRouter.StartListeningAsync();

        var (text, process) = await RunOneNarrationAsync(openRouter);

        Assert.False(text.StartsWith("ERROR:", StringComparison.Ordinal), text);
        var request = Assert.Single(openRouter.Requests);
        Assert.Equal($"Bearer {OpenRouterKey}", request.Authorization);
        AssertNoSecrets(process, OpenRouterKey, ClientSecret);
    }

    // TOR-03-A312w2t: OpenRouter answers 401; the result names the rejected key without revealing it, and
    // neither the result, stderr nor logs/ contains the key.
    [Fact]
    public async Task TOR_03_A312w2t_rejected_key_is_not_revealed_in_result_or_log()
    {
        await using var openRouter = await FakeOpenRouter.StartListeningAsync();
        openRouter.StatusOverride = _ => 401;

        var (text, process) = await RunOneNarrationAsync(openRouter);

        Assert.Equal("ERROR: the gateway's OpenRouter key was rejected. Ask an administrator to check the gateway's key.", text);
        Assert.DoesNotContain(OpenRouterKey, text, StringComparison.Ordinal);
        Assert.Single(openRouter.Requests);
        Assert.Contains(process.StderrLines, line => line.Contains("answered 401", StringComparison.Ordinal));
        AssertNoSecrets(process, OpenRouterKey, ClientSecret);
    }

    private static async Task<(string Text, ServerProcess Process)> RunOneNarrationAsync(FakeOpenRouter openRouter)
    {
        await using var stub = await StubOidcIssuer.StartAsync(ClientSecret);
        using var data = new TempDirectory();
        var port = FreePort.Next();
        var environment = GatewayProcess.ValidEnvironment(port, data);
        environment["Broker__UpstreamAuthority"] = stub.Authority;
        environment["Broker__UpstreamClientId"] = stub.ClientId;
        environment["Broker__UpstreamClientSecret"] = ClientSecret;
        environment["OpenRouter__ApiKey"] = OpenRouterKey;
        environment["OpenRouter__BaseUrl"] = openRouter.BaseUrl;
        environment["OpenRouter__RetryDelayMs"] = "1";
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

            var text = await CallSynthesizeAsync(gateway, signedIn.AccessToken);
            await process.StopAsync();
            return (text, process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private static async Task<string> CallSynthesizeAsync(HttpClient gateway, string accessToken)
    {
        const string body = """
            {"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"synthesize_narration","arguments":{"clips":[{"n":1,"text":"One clip says hello to everyone listening today."}]}}}
            """;
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

    private static void AssertNoSecrets(ServerProcess process, params string[] secrets)
    {
        using (process)
        {
            Assert.NotEmpty(process.StderrLines);
            var logDirectory = RepoPaths.LogDirectory(GatewayProcess.Project);
            var logFiles = Directory.Exists(logDirectory) ? Directory.GetFiles(logDirectory, "*", SearchOption.AllDirectories) : [];
            Assert.NotEmpty(logFiles);
            var logText = string.Join('\n', logFiles.Select(f => string.Join('\n', GatewayStartupLogTests.ReadLinesShared(f))));
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, process.Stderr, StringComparison.Ordinal);
                Assert.DoesNotContain(secret, logText, StringComparison.Ordinal);
            }
        }
    }
}
