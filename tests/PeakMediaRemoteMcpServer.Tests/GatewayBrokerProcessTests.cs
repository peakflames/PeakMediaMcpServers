namespace PeakMediaRemoteMcpServer.Tests;

[Collection("GatewayProcess")]
public class GatewayBrokerProcessTests
{
    // Epic 7BASFbp smoke test (TOR-02-QFiwMY7, TOR-01-p6jE1nT over real sockets): the built gateway binary
    // and the stub OIDC issuer on real ports; full sign-in, initialize succeeds, refresh works; neither
    // stderr nor the log file holds an access token, refresh token, authorization code or client secret.
    [Fact]
    public async Task Real_process_sign_in_opens_mcp_and_logs_no_secrets()
    {
        await using var stub = await StubOidcIssuer.StartAsync();
        using var data = new TempDirectory();
        var port = FreePort.Next();
        var environment = GatewayProcess.ValidEnvironment(port, data);
        environment["Broker__UpstreamAuthority"] = stub.Authority;
        environment["Broker__UpstreamClientId"] = stub.ClientId;
        environment["Broker__UpstreamClientSecret"] = stub.ClientSecret;
        environment["Admin__Group"] = TestSignIn.AdminGroup;
        var started = DateTime.UtcNow;
        using var process = GatewayProcess.Start(environment);
        using var gateway = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        await GatewayProcess.WaitUntilReadyAsync(process, gateway);

        stub.SetNextUser(StubUser.For("pat@example.com", TestSignIn.AdminGroup));
        var client = await TestSignIn.RegisterAsync(gateway);
        var verifier = TestSignIn.NewVerifier();
        var start = await TestSignIn.BeginAsync(gateway, client, "xyz", TestSignIn.Challenge(verifier));
        var clientRedirect = await TestSignIn.CallbackAsync(gateway, await TestSignIn.FollowProviderAsync(start.Location!), start.ConsentCookie);
        var code = TestSignIn.Query(clientRedirect)["code"]!;
        using var tokenResponse = await TestSignIn.ExchangeCodeAsync(gateway, client, code, verifier);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var tokens = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var accessToken = tokens.RootElement.GetProperty("access_token").GetString()!;
        var refreshToken = tokens.RootElement.GetProperty("refresh_token").GetString()!;

        using var initialize = await TestSignIn.InitializeAsync(gateway, accessToken);
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        using var refreshed = await TestSignIn.RefreshAsync(gateway, client, refreshToken);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        using var refreshedTokens = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());
        var secondAccessToken = refreshedTokens.RootElement.GetProperty("access_token").GetString()!;
        await process.StopAsync();

        var logFile = Path.Combine(RepoPaths.LogDirectory(GatewayProcess.Project), $"PeakMediaRemoteMcpServer_{started:yyyyMMdd}.log");
        var logText = string.Join('\n', GatewayStartupLogTests.ReadLinesShared(logFile));
        Assert.Contains(process.StderrLines, line => line.Contains("Sign-in completed with role admin", StringComparison.Ordinal));
        foreach (var secret in new[] { accessToken, secondAccessToken, refreshToken, code, client.ClientSecret, stub.ClientSecret, verifier })
        {
            Assert.DoesNotContain(secret, process.Stderr, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, logText, StringComparison.Ordinal);
        }

        Assert.Empty(process.StdoutLines);
    }
}
