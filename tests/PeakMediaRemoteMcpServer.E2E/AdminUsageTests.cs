namespace PeakMediaRemoteMcpServer.E2E;

[Collection(DashboardCollection.Name)]
public sealed class AdminUsageTests(BrowserFixture browser)
{
    private static ILocator Table(IPage page)
    {
        return page.Locator("main [data-users-table]");
    }

    private static ILocator Row(IPage page, string email)
    {
        return Table(page).Locator($"tbody tr[data-user='{email}']");
    }

    /// <summary>Registers each person, sets a $25 quota, and records their spend at the host clock (2026-11-12).</summary>
    private static async Task SeedAsync(DashboardHost host, params (string Email, decimal Spent, int Clips)[] people)
    {
        foreach (var (email, spent, clips) in people)
        {
            await host.RegisterUserAsync(email);
            await host.SetQuotaAsync(email, 25.00m);
            await host.SpendAsync(email, spent, clips);
        }
    }

    private static async Task OpenUsageAsync(DashboardHost host, IPage page)
    {
        await host.SignInAsAdminAsync(page);
        await DashboardHost.WaitInteractiveAsync(page);
        await page.GetByRole(AriaRole.Navigation, new() { Name = "Primary" }).GetByRole(AriaRole.Link, new() { Name = "Usage", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Usage", Exact = true, Level = 1 })).ToBeVisibleAsync();
        // The first load has finished (the page is interactive and shows a result).
        await Assertions.Expect(page.Locator("main [data-state='populated'], main [data-state='empty'], main [data-state='error']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TOR_08_kX399h8_one_row_per_user_sorted_by_spent()
    {
        // TOR-08-kX399h8 — clock 2026-11-12
        await using var host = await DashboardHost.StartAsync();
        await SeedAsync(host, ("lee@example.com", 0.40m, 2), ("pat@example.com", 25.32m, 3), ("sam@example.com", 12.34m, 4));
        var page = await browser.NewPageAsync();
        await OpenUsageAsync(host, page);
        await page.Locator("#month-select").SelectOptionAsync("2026-11");

        await Assertions.Expect(Table(page).Locator("thead th")).ToHaveTextAsync(["Email", "Spent", "Quota", "Characters", "Clips", "Cache-hit rate", "Status"]);
        await Assertions.Expect(Table(page).Locator("tbody tr")).ToHaveCountAsync(3);
        await Assertions.Expect(Table(page).Locator("tbody tr th")).ToHaveTextAsync(["pat@example.com", "sam@example.com", "lee@example.com"]);
        await Assertions.Expect(page.Locator("main [data-subtitle]")).ToHaveTextAsync("2026-11 · 3 users · 1 over quota · sorted by Spent, highest first");
        Assert.Equal("Usage · PeakMedia Gateway", await page.TitleAsync());
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_nzR0l2p_over_badge_where_spend_reaches_quota()
    {
        // TOR-08-nzR0l2p
        await using var host = await DashboardHost.StartAsync();
        await SeedAsync(host, ("pat@example.com", 25.32m, 3), ("kim@example.com", 25.00m, 2), ("sam@example.com", 12.34m, 4));
        var page = await browser.NewPageAsync();
        await OpenUsageAsync(host, page);

        await Assertions.Expect(Row(page, "pat@example.com").Locator("[data-status]")).ToContainTextAsync("OVER");
        await Assertions.Expect(Row(page, "kim@example.com").Locator("[data-status]")).ToContainTextAsync("OVER");
        await Assertions.Expect(Row(page, "sam@example.com").Locator("[data-status]")).Not.ToContainTextAsync("OVER");
        await Assertions.Expect(Row(page, "pat@example.com").Locator("[data-th='Spent']")).ToHaveTextAsync("$25.32");
        await Assertions.Expect(Row(page, "kim@example.com").Locator("[data-th='Quota']")).ToHaveTextAsync("$25.00");
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_nFpaAG7_total_footer_row()
    {
        // TOR-08-nFpaAG7
        await using var host = await DashboardHost.StartAsync();
        await SeedAsync(host, ("pat@example.com", 25.32m, 300), ("sam@example.com", 12.34m, 120), ("lee@example.com", 0.40m, 6));
        var page = await browser.NewPageAsync();
        await OpenUsageAsync(host, page);

        var footer = Table(page).Locator("tfoot tr");
        await Assertions.Expect(footer.Locator("th")).ToHaveTextAsync("Total");
        await Assertions.Expect(footer.Locator("[data-th='Spent']")).ToHaveTextAsync("$38.06");
        await Assertions.Expect(footer.Locator("[data-th='Clips']")).ToHaveTextAsync("426");
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_rnr0ApF_empty_month_and_go_to_current_month()
    {
        // TOR-08-rnr0ApF — usage only in 2026-11, none in 2026-09
        await using var host = await DashboardHost.StartAsync();
        await SeedAsync(host, ("pat@example.com", 1.00m, 1));
        var page = await browser.NewPageAsync();
        await OpenUsageAsync(host, page);

        await page.Locator("#month-select").SelectOptionAsync("2026-09");

        var main = page.Locator("main");
        await Assertions.Expect(main.GetByText("No usage in 2026-09")).ToBeVisibleAsync();
        await Assertions.Expect(main.GetByText("Nobody requested narration in this month.")).ToBeVisibleAsync();
        var current = main.GetByRole(AriaRole.Button, new() { Name = "Go to current month" });
        await Assertions.Expect(current).ToBeVisibleAsync();

        await current.ClickAsync();

        await Assertions.Expect(page.Locator("#month-select")).ToHaveValueAsync("2026-11");
        await Assertions.Expect(Table(page)).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_S8MYBHC_error_state_and_working_retry()
    {
        // TOR-08-S8MYBHC
        await using var host = await DashboardHost.StartAsync();
        await SeedAsync(host, ("pat@example.com", 1.00m, 1));
        var page = await browser.NewPageAsync();
        await host.SignInAsAdminAsync(page);
        host.SetStorageFault(new StorageFaultSwitch(fail: true, TimeSpan.Zero));

        await page.GotoAsync(host.BaseUrl + "/admin");

        var main = page.Locator("main");
        await Assertions.Expect(main.GetByText("Couldn't load usage for 2026-11. Check your connection and try again.")).ToBeVisibleAsync();
        var retry = main.GetByRole(AriaRole.Button, new() { Name = "Retry" });
        await Assertions.Expect(retry).ToBeVisibleAsync();
        Assert.Equal(0, await Table(page).CountAsync());

        host.SetStorageFault(StorageFaultSwitch.None);
        await retry.ClickAsync();

        await Assertions.Expect(Table(page)).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Loading_state_shows_within_one_second()
    {
        // UX Baseline (Screen states, Progress feedback) on Usage (S-02).
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsAdminAsync(page);
        host.SetStorageFault(new StorageFaultSwitch(fail: false, TimeSpan.FromSeconds(3)));

        await page.GotoAsync(host.BaseUrl + "/admin", new() { WaitUntil = WaitUntilState.Commit });

        await Assertions.Expect(page.Locator("main").GetByText("Loading usage for 2026-11…")).ToBeVisibleAsync(new() { Timeout = 1000 });
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Table_collapses_to_labelled_rows_at_320px_without_horizontal_scroll()
    {
        // UX Baseline (Layout floor) on Usage (S-02).
        await using var host = await DashboardHost.StartAsync();
        await SeedAsync(host, ("pat@example.com", 25.32m, 3), ("sam@example.com", 12.34m, 4));
        var page = await browser.NewPageAsync(width: 320, height: 800);
        await host.SignInAsAdminAsync(page);
        await page.GotoAsync(host.BaseUrl + "/admin");
        await Assertions.Expect(Table(page)).ToBeVisibleAsync();

        Assert.False(await page.Locator("main thead").IsVisibleAsync());
        var overflow = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.False(overflow, "the Usage screen scrolls horizontally at 320 px");
        await page.Context.CloseAsync();
    }
}
