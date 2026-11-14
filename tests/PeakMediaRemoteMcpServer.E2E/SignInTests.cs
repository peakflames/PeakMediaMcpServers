namespace PeakMediaRemoteMcpServer.E2E;

[Collection(DashboardCollection.Name)]
public sealed class SignInTests(BrowserFixture browser)
{
    private static readonly StubUser Pat = StubUser.For("pat@example.com");

    [Fact]
    public async Task TOR_08_7gPJfwq_no_session_is_redirected_to_the_provider_before_any_usage_is_sent()
    {
        // TOR-08-7gPJfwq
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync("pat@example.com");
        await host.SpendAsync("pat@example.com", 12.34m, clips: 40, cacheHits: 9);

        // The gateway's own answer to "/" with no cookie: a redirect to the provider, no page body.
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(host.BaseUrl) };
        using var response = await http.GetAsync("/");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(host.Stub.AuthorizationEndpoint + "?", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("$12.34", body, StringComparison.Ordinal);
        Assert.DoesNotContain("My usage", body, StringComparison.Ordinal);

        // And in a browser: the first navigation goes to the provider's authorization endpoint.
        var page = await browser.NewPageAsync();
        var providerRequests = new List<string>();
        page.Request += (_, request) =>
        {
            if (request.Url.StartsWith(host.Stub.AuthorizationEndpoint, StringComparison.Ordinal))
            {
                providerRequests.Add(request.Url);
            }
        };
        await host.SignInAsync(page, Pat);
        Assert.Single(providerRequests);
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_RiNk9re_sign_in_lands_on_my_usage_with_email_sign_out_and_version_footer()
    {
        // TOR-08-RiNk9re
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();

        await host.SignInAsync(page, Pat);

        await Assertions.Expect(page).ToHaveURLAsync(host.BaseUrl + "/");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "My usage", Level = 1 })).ToBeVisibleAsync();
        var header = page.Locator("header[data-app-header]");
        await Assertions.Expect(header).ToContainTextAsync("pat@example.com");
        await Assertions.Expect(header.GetByRole(AriaRole.Button, new() { Name = "Sign out" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("footer")).ToContainTextAsync(new Regex(@"PeakMediaRemoteMcpServer v\d+\.\d+\.\d+"));
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_F0Gau1p_sign_out_shows_signed_out_and_ends_the_browser_session()
    {
        // TOR-08-F0Gau1p
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsync(page, Pat);
        await DashboardHost.WaitInteractiveAsync(page);

        await page.Locator("header[data-app-header]").GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();

        await Assertions.Expect(page).ToHaveURLAsync(host.BaseUrl + "/signed-out");
        await Assertions.Expect(page.GetByText("You are signed out")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Sign in" })).ToBeVisibleAsync();

        // Opening "/" afterwards goes to the provider's sign-in page again.
        var providerRequests = 0;
        page.Request += (_, request) =>
        {
            if (request.Url.StartsWith(host.Stub.AuthorizationEndpoint, StringComparison.Ordinal))
            {
                providerRequests++;
            }
        };
        await page.GotoAsync(host.BaseUrl + "/");
        Assert.Equal(1, providerRequests);
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_qmHrQZN_sign_in_link_on_signed_out_starts_a_new_sign_in()
    {
        // TOR-08-qmHrQZN
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/signed-out");
        await Assertions.Expect(page.GetByText("You are signed out")).ToBeVisibleAsync();

        // The stub provider approves at once, so record the browser's navigation through its authorization endpoint.
        var providerRequests = new List<string>();
        page.Request += (_, request) =>
        {
            if (request.IsNavigationRequest && request.Url.StartsWith(host.Stub.AuthorizationEndpoint + "?", StringComparison.Ordinal))
            {
                providerRequests.Add(request.Url);
            }
        };
        await page.GetByRole(AriaRole.Link, new() { Name = "Sign in" }).ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "My usage", Level = 1 })).ToBeVisibleAsync();
        var authorize = Assert.Single(providerRequests);
        Assert.Contains("client_id=" + host.Stub.ClientId, authorize, StringComparison.Ordinal);
        await page.Context.CloseAsync();
    }
}
