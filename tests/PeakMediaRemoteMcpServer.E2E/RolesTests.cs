namespace PeakMediaRemoteMcpServer.E2E;

[Collection(DashboardCollection.Name)]
public sealed class RolesTests(BrowserFixture browser)
{
    private static readonly StubUser Pat = StubUser.For("pat@example.com", "Everyone");

    [Fact]
    public async Task TOR_08_XOxyf4r_non_admin_sees_only_the_my_usage_link()
    {
        // TOR-08-XOxyf4r
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsync(page, Pat);
        await DashboardHost.WaitInteractiveAsync(page);

        var nav = page.GetByRole(AriaRole.Navigation, new() { Name = "Primary" });
        await Assertions.Expect(nav.GetByRole(AriaRole.Link, new() { Name = "My usage", Exact = true })).ToBeVisibleAsync();
        // No "Usage" or "Cache" link anywhere on the page, enabled or disabled (hidden ones included).
        Assert.Equal(0, await page.Locator("a").Filter(new() { HasTextRegex = new Regex(@"^\s*(Usage|Cache)\s*$") }).CountAsync());
        Assert.Equal(0, await page.Locator("a[href^='/admin'], a[aria-disabled]").CountAsync());
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_XOxyf4r_admin_group_member_sees_the_administrator_links()
    {
        // TOR-08-XOxyf4r (contrast case: the links exist only for the Admin:Group)
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsync(page, StubUser.For("ada@example.com", DashboardHost.AdminGroup));

        var nav = page.GetByRole(AriaRole.Navigation, new() { Name = "Primary" });
        await Assertions.Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Usage", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Cache", Exact = true })).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_iCZ63da_every_admin_address_answers_403_not_authorized_without_other_users_data()
    {
        // TOR-08-iCZ63da
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync("ann@example.com");
        await host.SpendAsync("ann@example.com", 3.00m, clips: 2);
        var page = await browser.NewPageAsync();
        await host.SignInAsync(page, Pat);

        var annId = StubUser.For("ann@example.com").Subject;
        foreach (var path in new[] { "/admin", "/admin/cache", $"/admin/users/{annId}" })
        {
            var response = await page.GotoAsync(host.BaseUrl + path);
            Assert.NotNull(response);
            Assert.Equal(403, response!.Status);
            await Assertions.Expect(page.GetByText("This page is for gateway administrators.")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Go to My usage" })).ToBeVisibleAsync();

            var html = await page.ContentAsync();
            Assert.DoesNotContain("ann@example.com", html, StringComparison.Ordinal);
            Assert.DoesNotMatch(new Regex(@"\$\d"), await page.InnerTextAsync("body"));
        }

        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_Z8HzUrl_go_to_my_usage_link_leads_from_not_authorized_to_my_usage()
    {
        // TOR-08-Z8HzUrl
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsync(page, Pat);
        await page.GotoAsync(host.BaseUrl + "/admin");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Not authorized" })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Link, new() { Name = "Go to My usage" }).ClickAsync();

        await Assertions.Expect(page).ToHaveURLAsync(host.BaseUrl + "/");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "My usage", Level = 1 })).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }
}
