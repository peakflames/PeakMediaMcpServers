namespace PeakMediaRemoteMcpServer.E2E;

[Collection(DashboardCollection.Name)]
public sealed class ThemeTests(BrowserFixture browser)
{
    [Fact]
    public async Task TOR_08_fX9XW5F_dark_theme_applies_the_dark_class_changes_the_background_and_survives_reload()
    {
        // TOR-08-fX9XW5F
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync(scheme: ColorScheme.Light);
        await host.SignInAsync(page, StubUser.For("pat@example.com"));
        await DashboardHost.WaitInteractiveAsync(page);

        var theme = page.GetByLabel("Theme");
        // The choice offers light, dark and system.
        Assert.Equal(["Light", "Dark", "System"], await theme.Locator("option").AllInnerTextsAsync());
        await theme.SelectOptionAsync("light");
        await Assertions.Expect(page.Locator("html")).Not.ToHaveClassAsync(new Regex(@"\bdark\b"));
        var lightBackground = await BodyBackgroundAsync(page);

        await theme.SelectOptionAsync("dark");

        await Assertions.Expect(page.Locator("html")).ToHaveClassAsync(new Regex(@"\bdark\b"));
        Assert.NotEqual(lightBackground, await BodyBackgroundAsync(page));

        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("html")).ToHaveClassAsync(new Regex(@"\bdark\b"));
        Assert.NotEqual(lightBackground, await BodyBackgroundAsync(page));
        await DashboardHost.WaitInteractiveAsync(page);
        await Assertions.Expect(page.GetByLabel("Theme")).ToHaveValueAsync("dark");

        // System follows the OS preference again (light here).
        await page.GetByLabel("Theme").SelectOptionAsync("system");
        await Assertions.Expect(page.Locator("html")).Not.ToHaveClassAsync(new Regex(@"\bdark\b"));
        await page.Context.CloseAsync();
    }

    private static Task<string> BodyBackgroundAsync(IPage page)
    {
        return page.EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor");
    }
}
