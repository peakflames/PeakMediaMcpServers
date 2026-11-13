namespace PeakMediaRemoteMcpServer.Tests;

public class SignedInHandshakeTests
{
    // TOR-01-p6jE1nT: a signed-in caller's initialize reports serverInfo.name PeakMediaRemoteMcpServer and
    // serverInfo.version equal to GET /version's version.
    [Fact]
    public async Task TOR_01_p6jE1nT_signed_in_initialize_reports_version_endpoint_version()
    {
        await using var stub = await StubOidcIssuer.StartAsync();
        await using var host = await GatewayTestHost.StartAsync(stub);
        var signedIn = await TestSignIn.SignInAsync(host.Client, stub, StubUser.For("pat@example.com"));
        using var versionResponse = await host.Client.GetAsync("/version");
        using var version = JsonDocument.Parse(await versionResponse.Content.ReadAsStringAsync());

        using var initialize = await TestSignIn.InitializeAsync(host.Client, signedIn.AccessToken);

        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        using var result = await TestSignIn.ReadJsonRpcAsync(initialize);
        var serverInfo = result.RootElement.GetProperty("result").GetProperty("serverInfo");
        Assert.Equal("PeakMediaRemoteMcpServer", serverInfo.GetProperty("name").GetString());
        Assert.Equal(version.RootElement.GetProperty("version").GetString(), serverInfo.GetProperty("version").GetString());
    }
}
