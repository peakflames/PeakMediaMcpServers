namespace PeakMediaRemoteMcpServer.E2E;

[Collection(DashboardCollection.Name)]
public sealed class AdminCacheTests(BrowserFixture browser)
{
    private const long GiB = 1024L * 1024 * 1024;

    private static readonly Dictionary<string, string?> TwentyGbCap = new() { ["Storage:CacheMaxGb"] = "20" };

    private static ILocator Card(IPage page, string card)
    {
        return page.Locator($"main [data-stat='{card}'] .stat-value");
    }

    [Fact]
    public async Task TOR_08_VHkMu93_cache_figures_on_the_stat_cards()
    {
        // TOR-08-VHkMu93 — 12.4 GB in 5,210 clips, 20 GB cap, 62 hits of 100 requested clips, 37 evictions (2026-11)
        await using var host = await DashboardHost.StartAsync(overrides: TwentyGbCap);
        await host.SeedCacheAsync(5210, (long)Math.Round(12.4 * GiB));
        await host.RegisterUserAsync("pat@example.com");
        await host.SpendAsync("pat@example.com", 1.00m, clips: 38, cacheHits: 62);
        await host.SeedEvictionsAsync(37);
        var page = await browser.NewPageAsync();
        await host.SignInAsAdminAsync(page);
        await DashboardHost.WaitInteractiveAsync(page);

        await page.GetByRole(AriaRole.Navigation, new() { Name = "Primary" }).GetByRole(AriaRole.Link, new() { Name = "Cache", Exact = true }).ClickAsync();

        var expected = new Dictionary<string, string>
        {
            ["Size"] = "12.4 GB",
            ["Size cap"] = "20 GB",
            ["Clips"] = "5,210",
            ["Hit rate this month"] = "62%",
            ["Evicted this month"] = "37",
        };
        foreach (var (card, value) in expected)
        {
            await Assertions.Expect(Card(page, card)).ToHaveTextAsync(value);
        }

        await Assertions.Expect(page.Locator("main [data-subtitle]")).ToHaveTextAsync("12.4 GB of 20 GB used (62%) · least-recently-used clips are removed first when full");
        Assert.Equal("Cache — PeakMedia Gateway", await page.TitleAsync());
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_5ZEXDBu_empty_cache_state()
    {
        // TOR-08-5ZEXDBu
        await using var host = await DashboardHost.StartAsync(overrides: TwentyGbCap);
        var page = await browser.NewPageAsync();
        await host.SignInAsAdminAsync(page);

        await page.GotoAsync(host.BaseUrl + "/admin/cache");

        var main = page.Locator("main");
        await Assertions.Expect(main.GetByText("The cache is empty")).ToBeVisibleAsync();
        await Assertions.Expect(main.GetByText("Clips appear here after the first narration request.")).ToBeVisibleAsync();
        await Assertions.Expect(main.GetByRole(AriaRole.Button, new() { Name = "Refresh" })).ToBeVisibleAsync();
        await Assertions.Expect(main.Locator("[data-subtitle]")).ToHaveTextAsync("0 GB of 20 GB used");
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_NeOMGCI_error_state_and_working_retry()
    {
        // TOR-08-NeOMGCI
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsAdminAsync(page);
        host.SetStorageFault(new StorageFaultSwitch(fail: true, TimeSpan.Zero));

        await page.GotoAsync(host.BaseUrl + "/admin/cache");

        var main = page.Locator("main");
        await Assertions.Expect(main.GetByText("Couldn't load cache statistics. Check your connection and try again.")).ToBeVisibleAsync();
        var retry = main.GetByRole(AriaRole.Button, new() { Name = "Retry" });
        await Assertions.Expect(retry).ToBeVisibleAsync();
        Assert.Equal(0, await main.Locator("[data-stat]").CountAsync());

        host.SetStorageFault(StorageFaultSwitch.None);
        await retry.ClickAsync();

        await Assertions.Expect(main.Locator("[data-stat-cards]")).ToBeVisibleAsync();
        await Assertions.Expect(main.Locator("[data-stat]")).ToHaveCountAsync(5);
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_irZrgRw_refresh_reloads_the_figures()
    {
        // TOR-08-irZrgRw
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsAdminAsync(page);
        await page.GotoAsync(host.BaseUrl + "/admin/cache");
        await Assertions.Expect(Card(page, "Clips")).ToHaveTextAsync("0");

        await host.SeedCacheAsync(1, 153_644);
        await page.Locator("main").GetByRole(AriaRole.Button, new() { Name = "Refresh" }).ClickAsync();

        await Assertions.Expect(Card(page, "Clips")).ToHaveTextAsync("1");
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Loading_state_shows_within_one_second()
    {
        // UX Baseline (Screen states, Progress feedback) on Cache (S-04).
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsAdminAsync(page);
        host.SetStorageFault(new StorageFaultSwitch(fail: false, TimeSpan.FromSeconds(3)));

        await page.GotoAsync(host.BaseUrl + "/admin/cache", new() { WaitUntil = WaitUntilState.Commit });

        await Assertions.Expect(page.Locator("main").GetByText("Loading cache statistics…")).ToBeVisibleAsync(new() { Timeout = 1000 });
        await page.Context.CloseAsync();
    }
}
