namespace PeakMediaRemoteMcpServer.E2E;

[Collection(DashboardCollection.Name)]
public sealed class UxStatesTests(BrowserFixture browser)
{
    private const string PatEmail = "pat@example.com";
    private const string LoadingText = "Loading your usage…";
    private static readonly StubUser Pat = StubUser.For(PatEmail);
    private static readonly string[] Cards = ["Spent", "Quota", "Remaining", "Clips synthesized", "Cache hits", "Resets"];

    [Fact]
    public async Task TOR_09_upSE1xN_loading_text_within_one_second_and_no_figures_until_loaded()
    {
        // TOR-09-upSE1xN
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsync(page, Pat);
        await host.SpendAsync(PatEmail, 3.10m, clips: 2);
        host.SetStorageFault(new StorageFaultSwitch(fail: false, TimeSpan.FromSeconds(3)));

        var started = DateTime.UtcNow;
        await page.GotoAsync(host.BaseUrl + "/", new() { WaitUntil = WaitUntilState.Commit });
        await Assertions.Expect(page.Locator("main").GetByText(LoadingText)).ToBeVisibleAsync(new() { Timeout = 1000 });
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1), "loading text took longer than 1 s");

        // While loading, no stat value is visible.
        while (await page.Locator("main").GetByText(LoadingText).IsVisibleAsync())
        {
            Assert.Equal(0, await page.Locator("main .stat-value").CountAsync());
            await Task.Delay(100);
        }

        await Assertions.Expect(MyUsageTests.StatValue(page, "Spent")).ToHaveTextAsync("$3.10");
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_09_9Orj70T_empty_month_says_so_with_the_quota_and_a_current_month_button()
    {
        // TOR-09-9Orj70T — clock 2026-11-12 10:00 UTC, no usage
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsync(page, Pat);
        await host.SetQuotaAsync(PatEmail, 25.00m);
        await page.ReloadAsync();

        var main = page.Locator("main");
        await Assertions.Expect(main.GetByText("No usage in 2026-11")).ToBeVisibleAsync();
        await Assertions.Expect(main.GetByText("Narration you request through the peak-media tools in Claude Code appears here. Your quota is $25.00 for the month.")).ToBeVisibleAsync();
        await Assertions.Expect(main.GetByRole(AriaRole.Button, new() { Name = "Go to current month" })).ToBeVisibleAsync();
        await Assertions.Expect(main.GetByText(LoadingText)).Not.ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_09_gadlg25_failed_load_shows_the_error_and_retry_and_no_figures()
    {
        // TOR-09-gadlg25
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsync(page, Pat);
        await host.SpendAsync(PatEmail, 3.10m, clips: 2);
        host.SetStorageFault(new StorageFaultSwitch(fail: true, TimeSpan.Zero));

        await page.GotoAsync(host.BaseUrl + "/");

        var main = page.Locator("main");
        await Assertions.Expect(main.GetByText("Couldn't load your usage. Check your connection and try again.")).ToBeVisibleAsync();
        await Assertions.Expect(main.GetByRole(AriaRole.Button, new() { Name = "Retry" })).ToBeVisibleAsync();
        Assert.Equal(0, await main.Locator("[data-stat]").CountAsync());
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_09_ZbKGrFs_populated_state_shows_six_cards_and_no_other_state_text()
    {
        // TOR-09-ZbKGrFs
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsync(page, Pat);
        await host.SpendAsync(PatEmail, 3.10m, clips: 2, cacheHits: 1);

        await page.GotoAsync(host.BaseUrl + "/");

        foreach (var card in Cards)
        {
            await Assertions.Expect(page.Locator($"main [data-stat='{card}']")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator($"main [data-stat='{card}'] .stat-label")).ToHaveTextAsync(card);
        }

        var main = page.Locator("main");
        await Assertions.Expect(main.GetByText(LoadingText)).Not.ToBeVisibleAsync();
        await Assertions.Expect(main.GetByText("No usage in")).Not.ToBeVisibleAsync();
        await Assertions.Expect(main.GetByText("Couldn't load your usage.")).Not.ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_09_NtBVF41_cancel_is_offered_after_ten_seconds_and_stops_the_load()
    {
        // TOR-09-NtBVF41
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsync(page, Pat);
        await host.SpendAsync(PatEmail, 3.10m, clips: 2);
        host.SetStorageFault(new StorageFaultSwitch(fail: false, TimeSpan.FromSeconds(15)));

        await page.GotoAsync(host.BaseUrl + "/");
        var main = page.Locator("main");
        await Assertions.Expect(main.GetByText(LoadingText)).ToBeVisibleAsync();
        var cancel = main.GetByRole(AriaRole.Button, new() { Name = "Cancel" });
        await Assertions.Expect(cancel).ToBeVisibleAsync(new() { Timeout = 12_000 });
        // Next to the loading text: in the same row.
        await Assertions.Expect(main.Locator(".loading-row").Filter(new() { HasText = LoadingText }).GetByRole(AriaRole.Button, new() { Name = "Cancel" })).ToBeVisibleAsync();

        await cancel.ClickAsync();

        await Assertions.Expect(main.GetByText(LoadingText)).Not.ToBeVisibleAsync();
        await Assertions.Expect(main.GetByText("Loading was cancelled.")).ToBeVisibleAsync();
        await Assertions.Expect(main.GetByRole(AriaRole.Button, new() { Name = "Retry" })).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }
}
