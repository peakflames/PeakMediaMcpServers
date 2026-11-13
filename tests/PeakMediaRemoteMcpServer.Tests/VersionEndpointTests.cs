namespace PeakMediaRemoteMcpServer.Tests;

public class VersionEndpointTests
{
    // TOR-01-9TZEFBD: GET /version with no Authorization header and no cookie → 200, JSON
    // {"name":"PeakMediaRemoteMcpServer","version":"<semver>"}.
    [Fact]
    public async Task Get_version_without_credentials_returns_name_and_semver()
    {
        await using var host = await GatewayTestHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/version");
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PeakMediaRemoteMcpServer", document.RootElement.GetProperty("name").GetString());
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), document.RootElement.GetProperty("version").GetString());
    }
}
