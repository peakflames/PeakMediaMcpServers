namespace PeakMediaRemoteMcpServer.Tests.Dashboard;

// Regression for the dashboard wiring: /version and the /mcp gate answer exactly as before, and the
// dashboard cookie can never authenticate /mcp.
public class ShellServedTests
{
    [Fact]
    public async Task Version_and_mcp_gate_are_unchanged_by_the_dashboard()
    {
        await using var host = await GatewayTestHost.StartAsync();

        using var version = await host.Client.GetAsync("/version");
        Assert.Equal(HttpStatusCode.OK, version.StatusCode);

        using var mcp = await TestSignIn.InitializeAsync(host.Client, accessToken: null);
        Assert.Equal(HttpStatusCode.Unauthorized, mcp.StatusCode);
        Assert.Contains("resource_metadata=", mcp.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/mcp", null, JwtBearerSchemeName)]
    [InlineData("/MCP/anything", null, JwtBearerSchemeName)]
    [InlineData("/", "Bearer abc", JwtBearerSchemeName)]
    [InlineData("/", null, DashboardAuthSetup.CookieScheme)]
    [InlineData("/admin/cache", null, DashboardAuthSetup.CookieScheme)]
    [InlineData("/_blazor", null, DashboardAuthSetup.CookieScheme)]
    public void Mcp_and_bearer_requests_never_read_the_dashboard_cookie(string path, string? authorization, string expected)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Path = path;
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        Assert.Equal(expected, DashboardAuthSetup.SelectScheme(context));
    }

    private const string JwtBearerSchemeName = "Bearer";
}
