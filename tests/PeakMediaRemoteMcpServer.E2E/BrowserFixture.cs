namespace PeakMediaRemoteMcpServer.E2E;

/// <summary>One headless Chromium for the whole run; installed on first use if it is missing.</summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    public IPlaywright Playwright { get; private set; } = default!;

    public IBrowser Browser { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        var installed = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        Assert.Equal(0, installed);
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    /// <summary>A fresh browser context (no cookies, empty localStorage), light color scheme unless told otherwise.</summary>
    public async Task<IPage> NewPageAsync(int width = 1280, int height = 800, ColorScheme scheme = ColorScheme.Light)
    {
        var context = await Browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = height }, ColorScheme = scheme });
        context.SetDefaultTimeout(15_000);
        return await context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await Browser.DisposeAsync();
        Playwright.Dispose();
    }
}

[CollectionDefinition(Name)]
public sealed class DashboardCollection : ICollectionFixture<BrowserFixture>
{
    public const string Name = "dashboard";
}
