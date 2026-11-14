namespace PeakMediaRemoteMcpServer.Tests.Dashboard;

public class DashboardUsageServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static async Task<ClaimsPrincipal> PatSessionAsync(GatewayTestHost host)
    {
        var subject = await UsageSeed.SubjectOfAsync(host);
        return DashboardAuthSetup.SessionPrincipal(subject, "pat@example.com", isAdmin: false);
    }

    // TOR-08-v8yv7CR: the dashboard's figures for pat equal my_usage's for the same caller, and
    // another user's spend is not counted.
    [Fact]
    public async Task TOR_08_v8yv7CR_dashboard_figures_equal_my_usage_for_the_same_caller()
    {
        await using var client = await NarrationClient.StartAsync(now: Now);
        var host = client.Host;
        await host.Services.GetRequiredService<UserStore>().UpsertSignInAsync("00uann-other-user", "ann@example.com", false, Now);
        await UsageSeed.SpendAsync(host, 3.00m, clips: 3, email: "ann@example.com");
        await UsageSeed.SetQuotaAsync(host, 25.00m);
        await UsageSeed.SpendAsync(host, 12.34m, clips: 40, cacheHits: 9);

        var service = host.Services.GetRequiredService<DashboardUsageService>();
        var dashboard = await service.GetAsync(await PatSessionAsync(host), "2026-11", CancellationToken.None);
        var tool = NarrationClient.UsageTable((await client.MyUsageAsync()).Text);

        Assert.True(dashboard.IsSuccess);
        var status = dashboard.Value;
        Assert.Equal("$12.34", UsageText.Dollars(status.Spent));
        Assert.Equal("$25.00", UsageText.Dollars(status.Quota));
        Assert.Equal("$12.66", UsageText.Dollars(status.Remaining));
        Assert.Equal(40, status.ClipsSynthesized);
        Assert.Equal(9, status.CacheHits);
        Assert.Equal("2026-12-01 00:00 UTC", UsageText.Utc(status.ResetsUtc));

        Assert.Equal(tool["spent"], UsageText.Dollars(status.Spent));
        Assert.Equal(tool["quota"], UsageText.Dollars(status.Quota));
        Assert.Equal(tool["remaining"], UsageText.Dollars(status.Remaining));
        Assert.Equal(tool["clips synthesized"], status.ClipsSynthesized.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(tool["cache hits"], status.CacheHits.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(tool["resets"], UsageText.Utc(status.ResetsUtc));
    }

    // TOR-08-TZrQMGu (service part): each month shows its own spend.
    [Fact]
    public async Task TOR_08_TZrQMGu_each_month_returns_its_own_spend()
    {
        await using var client = await NarrationClient.StartAsync(now: new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var host = client.Host;
        await UsageSeed.SpendAsync(host, 4.00m, clips: 2);
        client.Clock.SetUtcNow(Now);
        await UsageSeed.SpendAsync(host, 12.34m, clips: 5);

        var service = host.Services.GetRequiredService<DashboardUsageService>();
        var pat = await PatSessionAsync(host);

        Assert.Equal("2026-11", service.CurrentMonthLabel);
        Assert.Equal(12, service.MonthLabels().Count);
        Assert.Equal("2026-11", service.MonthLabels()[0]);
        Assert.Equal("2025-12", service.MonthLabels()[^1]);
        Assert.Equal(4.00m, (await service.GetAsync(pat, "2026-10", CancellationToken.None)).Value.Spent);
        Assert.Equal(12.34m, (await service.GetAsync(pat, "2026-11", CancellationToken.None)).Value.Spent);
    }

    // The month comes from the list shown; anything else is refused. The caller comes from the session only.
    [Fact]
    public async Task Unlisted_month_and_signed_out_principal_are_refused()
    {
        await using var client = await NarrationClient.StartAsync(now: Now);
        var service = client.Host.Services.GetRequiredService<DashboardUsageService>();

        Assert.True((await service.GetAsync(await PatSessionAsync(client.Host), "2027-01", CancellationToken.None)).IsFailed);
        Assert.True((await service.GetAsync(new ClaimsPrincipal(), "2026-11", CancellationToken.None)).IsFailed);
    }
}
