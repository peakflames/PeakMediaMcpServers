namespace PeakMediaRemoteMcpServer.E2E;

[Collection(DashboardCollection.Name)]
public sealed class MyUsageTests(BrowserFixture browser)
{
    private const string PatEmail = "pat@example.com";
    private static readonly StubUser Pat = StubUser.For(PatEmail);

    internal static ILocator StatValue(IPage page, string card)
    {
        return page.Locator($"main [data-stat='{card}'] .stat-value");
    }

    [Fact]
    public async Task TOR_08_v8yv7CR_stat_cards_show_the_callers_own_figures()
    {
        // TOR-08-v8yv7CR — clock 2026-11-12 10:00 UTC
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync("ann@example.com");
        await host.SpendAsync("ann@example.com", 3.00m, clips: 3);
        var page = await browser.NewPageAsync();
        await host.SignInAsync(page, Pat);
        await host.SetQuotaAsync(PatEmail, 25.00m);
        await host.SpendAsync(PatEmail, 12.34m, clips: 40, cacheHits: 9);

        await page.ReloadAsync();

        var expected = new Dictionary<string, string>
        {
            ["Spent"] = "$12.34",
            ["Quota"] = "$25.00",
            ["Remaining"] = "$12.66",
            ["Clips synthesized"] = "40",
            ["Cache hits"] = "9",
            ["Resets"] = "2026-12-01 00:00 UTC",
        };
        foreach (var (card, value) in expected)
        {
            await Assertions.Expect(StatValue(page, card)).ToHaveTextAsync(value);
        }

        Assert.DoesNotContain("ann@example.com", await page.ContentAsync(), StringComparison.Ordinal);
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_TZrQMGu_month_select_shows_the_picked_months_figures()
    {
        // TOR-08-TZrQMGu
        // The usage clock starts in October, then moves on to 2026-11-12.
        await using var host = await DashboardHost.StartAsync(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var page = await browser.NewPageAsync();
        await host.SignInAsync(page, Pat);
        await host.SpendAsync(PatEmail, 4.00m, clips: 2);
        host.Clock.SetUtcNow(DashboardHost.DefaultNow);
        await host.SpendAsync(PatEmail, 12.34m, clips: 5);
        await page.ReloadAsync();
        await Assertions.Expect(page.GetByLabel("Month")).ToHaveValueAsync("2026-11");
        await Assertions.Expect(StatValue(page, "Spent")).ToHaveTextAsync("$12.34");

        await page.GetByLabel("Month").SelectOptionAsync("2026-10");

        await Assertions.Expect(StatValue(page, "Spent")).ToHaveTextAsync("$4.00");
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_0OVqiet_over_quota_notice_names_spend_quota_and_reset()
    {
        // TOR-08-0OVqiet — clock 2026-11-12 10:00 UTC
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsync(page, Pat);
        await host.SetQuotaAsync(PatEmail, 25.00m);
        await host.SpendAsync(PatEmail, 25.32m, clips: 4);

        await page.ReloadAsync();

        await Assertions.Expect(page.GetByText(
            "You have reached your monthly quota ($25.32 of $25.00). Narration is paused until 2026-12-01 00:00 UTC. Ask an administrator to raise your quota.",
            new() { Exact = true })).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_6kh4CnB_go_to_current_month_returns_to_the_current_month()
    {
        // TOR-08-6kh4CnB
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsync(page, Pat);
        await host.SpendAsync(PatEmail, 2.50m, clips: 3);
        await page.ReloadAsync();
        await Assertions.Expect(StatValue(page, "Spent")).ToHaveTextAsync("$2.50");

        await page.GetByLabel("Month").SelectOptionAsync("2026-09");
        await Assertions.Expect(page.GetByText("No usage in 2026-09")).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Go to current month" }).ClickAsync();

        await Assertions.Expect(page.GetByLabel("Month")).ToHaveValueAsync("2026-11");
        foreach (var card in new[] { "Spent", "Quota", "Remaining", "Clips synthesized", "Cache hits", "Resets" })
        {
            await Assertions.Expect(page.Locator($"main [data-stat='{card}']")).ToBeVisibleAsync();
        }

        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_rxZtbxT_retry_after_a_failed_load_shows_the_figures()
    {
        // TOR-08-rxZtbxT
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsync(page, Pat);
        await host.SpendAsync(PatEmail, 1.25m, clips: 2);
        host.SetStorageFault(new StorageFaultSwitch(fail: true, TimeSpan.Zero));
        await page.ReloadAsync();
        await Assertions.Expect(page.GetByText("Couldn't load your usage. Check your connection and try again.")).ToBeVisibleAsync();

        host.SetStorageFault(StorageFaultSwitch.None);
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry" }).ClickAsync();

        await Assertions.Expect(StatValue(page, "Spent")).ToHaveTextAsync("$1.25");
        await Assertions.Expect(page.GetByText("Couldn't load your usage.")).Not.ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }
}
