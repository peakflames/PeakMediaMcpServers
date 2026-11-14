namespace PeakMediaRemoteMcpServer.E2E;

[Collection(DashboardCollection.Name)]
public sealed class AdminNavTests(BrowserFixture browser)
{
    /// <summary>My Usage loads only over the live circuit, so a finished load means the page is interactive.</summary>
    private static async Task LoadedAsync(IPage page)
    {
        await Assertions.Expect(page.Locator("main [data-state]")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("main [data-state='loading']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task TOR_08_D1L4ilb_admin_sees_exactly_my_usage_usage_and_cache()
    {
        // TOR-08-D1L4ilb
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsAdminAsync(page);
        await DashboardHost.WaitInteractiveAsync(page);
        await LoadedAsync(page);

        var links = page.GetByRole(AriaRole.Navigation, new() { Name = "Primary" }).GetByRole(AriaRole.Link);
        await Assertions.Expect(links).ToHaveTextAsync(["My usage", "Usage", "Cache"]);
        Assert.Equal(3, await links.CountAsync());
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_leBAXoJ_below_480px_the_header_collapses_into_a_sheet()
    {
        // TOR-08-leBAXoJ — 400 CSS px wide
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync(width: 400, height: 800);
        await host.SignInAsAdminAsync(page);
        await DashboardHost.WaitInteractiveAsync(page);
        await LoadedAsync(page);

        var header = page.Locator("[data-app-header]");
        await Assertions.Expect(header.GetByRole(AriaRole.Link, new() { Name = "Usage", Exact = true })).Not.ToBeVisibleAsync();
        await Assertions.Expect(header.GetByRole(AriaRole.Link, new() { Name = "Cache", Exact = true })).Not.ToBeVisibleAsync();
        var menu = header.GetByRole(AriaRole.Button, new() { Name = "Open menu" });
        await Assertions.Expect(menu).ToBeVisibleAsync();

        await menu.ClickAsync();

        var sheet = page.Locator("[data-menu-sheet]");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        foreach (var name in new[] { "My usage", "Usage", "Cache" })
        {
            await Assertions.Expect(sheet.GetByRole(AriaRole.Link, new() { Name = name, Exact = true })).ToBeVisibleAsync();
        }

        await Assertions.Expect(sheet.GetByText("ada@example.com")).ToBeVisibleAsync();
        await Assertions.Expect(sheet.GetByRole(AriaRole.Button, new() { Name = "Sign out" })).ToBeVisibleAsync();

        // UX Baseline (Keyboard & focus): Escape closes the sheet and focus returns to the menu button.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(sheet).Not.ToBeVisibleAsync();
        await Assertions.Expect(menu).ToBeFocusedAsync();

        // A link in the sheet navigates and closes it.
        await menu.ClickAsync();
        await sheet.GetByRole(AriaRole.Link, new() { Name = "Cache", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Cache", Exact = true, Level = 1 })).ToBeVisibleAsync();
        await Assertions.Expect(sheet).Not.ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Sheet_holds_only_my_usage_for_a_non_admin()
    {
        // TOR-08-XOxyf4r at narrow width: the sheet offers no administrator link.
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync(width: 400, height: 800);
        await host.SignInAsync(page, StubUser.For("pat@example.com", "Everyone"));
        await DashboardHost.WaitInteractiveAsync(page);
        await LoadedAsync(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "Open menu" }).ClickAsync();

        var sheet = page.Locator("[data-menu-sheet]");
        await Assertions.Expect(sheet.GetByRole(AriaRole.Link, new() { Name = "My usage", Exact = true })).ToBeVisibleAsync();
        Assert.Equal(0, await page.Locator("a[href^='/admin']").CountAsync());
        await page.Context.CloseAsync();
    }
}
