namespace PeakMediaRemoteMcpServer.Tests.Dashboard;

public class AdminUsageServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static ClaimsPrincipal AdaSession()
    {
        return DashboardAuthSetup.SessionPrincipal("00uada-admin", "ada@example.com", isAdmin: true);
    }

    private static async Task<NarrationClient> StartWithUsersAsync(params string[] others)
    {
        var client = await NarrationClient.StartAsync(now: Now);
        foreach (var email in others)
        {
            await client.SignInAnotherAsync(email);
        }

        return client;
    }

    private static Task<FluentResults.Result<AdminUsageReport>> ReadAsync(NarrationClient client, string month = "2026-11", ClaimsPrincipal? principal = null)
    {
        return client.Host.Services.GetRequiredService<AdminUsageService>().GetAsync(principal ?? AdaSession(), month, CancellationToken.None);
    }

    // TOR-08-kX399h8: pat $25.32, sam $12.34, lee $0.40 in 2026-11 → 3 rows, Spent highest first, 1 over quota.
    [Fact]
    public async Task TOR_08_kX399h8_rows_sorted_by_spent_highest_first()
    {
        await using var client = await StartWithUsersAsync("sam@example.com", "lee@example.com");
        var host = client.Host;
        await UsageSeed.SetQuotaAsync(host, 25.00m);
        await UsageSeed.SetQuotaAsync(host, 25.00m, "sam@example.com");
        await UsageSeed.SetQuotaAsync(host, 25.00m, "lee@example.com");
        await UsageSeed.SpendAsync(host, 0.40m, clips: 2, email: "lee@example.com");
        await UsageSeed.SpendAsync(host, 25.32m, clips: 3);
        await UsageSeed.SpendAsync(host, 12.34m, clips: 4, email: "sam@example.com");

        var report = await ReadAsync(client);

        Assert.True(report.IsSuccess);
        Assert.Equal(["pat@example.com", "sam@example.com", "lee@example.com"], report.Value.Rows.Select(r => r.Email));
        Assert.Equal(1, report.Value.Total.OverCount);
    }

    // TOR-08-nzR0l2p: spent == quota counts as over; below quota does not.
    [Fact]
    public async Task TOR_08_nzR0l2p_spend_reaching_quota_is_over()
    {
        await using var client = await StartWithUsersAsync("kim@example.com", "sam@example.com");
        var host = client.Host;
        foreach (var email in new[] { "pat@example.com", "kim@example.com", "sam@example.com" })
        {
            await UsageSeed.SetQuotaAsync(host, 25.00m, email);
        }

        await UsageSeed.SpendAsync(host, 25.32m, clips: 2);
        await UsageSeed.SpendAsync(host, 25.00m, clips: 2, email: "kim@example.com");
        await UsageSeed.SpendAsync(host, 12.34m, clips: 2, email: "sam@example.com");

        var rows = (await ReadAsync(client)).Value.Rows.ToDictionary(r => r.Email);

        Assert.True(rows["pat@example.com"].IsOver);
        Assert.True(rows["kim@example.com"].IsOver);
        Assert.False(rows["sam@example.com"].IsOver);
        Assert.Equal(25.00m, rows["kim@example.com"].Quota);
    }

    // TOR-08-nFpaAG7: $25.32 + $12.34 + $0.40 on 300 + 120 + 6 clips → Total $38.06 and 426 clips.
    [Fact]
    public async Task TOR_08_nFpaAG7_total_sums_the_month()
    {
        await using var client = await StartWithUsersAsync("sam@example.com", "lee@example.com");
        var host = client.Host;
        await UsageSeed.SpendAsync(host, 25.32m, clips: 280, cacheHits: 20);
        await UsageSeed.SpendAsync(host, 12.34m, clips: 120, email: "sam@example.com");
        await UsageSeed.SpendAsync(host, 0.40m, clips: 6, email: "lee@example.com");

        var report = (await ReadAsync(client)).Value;

        Assert.Equal(38.06m, report.Total.Spent);
        Assert.Equal(426, report.Total.Clips);
        Assert.Equal(426L * 40, report.Total.Characters);
        Assert.Equal(300, report.Rows[0].Clips);
        Assert.Equal(7, report.Rows[0].CacheHitRatePercent);
        Assert.Equal(5, report.Total.CacheHitRatePercent);
    }

    // TOR-08-rnr0ApF: a month with no usage returns zero rows (the screen's empty state).
    [Fact]
    public async Task TOR_08_rnr0ApF_month_without_usage_has_no_rows()
    {
        await using var client = await StartWithUsersAsync();
        await UsageSeed.SpendAsync(client.Host, 1.00m);

        var report = await ReadAsync(client, "2026-09");

        Assert.True(report.IsSuccess);
        Assert.Empty(report.Value.Rows);
        Assert.Equal(0, report.Value.Total.Clips);
    }

    // TOR-08-S8MYBHC: with the test fault switch set, the read fails (the screen's error state).
    [Fact]
    public async Task TOR_08_S8MYBHC_fault_switch_fails_the_read()
    {
        await using var client = await StartWithUsersAsync();
        var factory = client.Host.Services.GetRequiredService<SqliteConnectionFactory>();
        factory.FaultSwitch = new StorageFaultSwitch(true, TimeSpan.Zero);

        Assert.True((await ReadAsync(client)).IsFailed);

        factory.FaultSwitch = StorageFaultSwitch.None;
        Assert.True((await ReadAsync(client)).IsSuccess);
    }

    // Access rule: a signed-in person without the admin role gets no rows, only a failure.
    [Fact]
    public async Task Non_admin_is_refused_and_sees_no_data()
    {
        await using var client = await StartWithUsersAsync();
        await UsageSeed.SpendAsync(client.Host, 1.00m);
        var pat = DashboardAuthSetup.SessionPrincipal(await UsageSeed.SubjectOfAsync(client.Host), "pat@example.com", isAdmin: false);

        var report = await ReadAsync(client, principal: pat);

        Assert.True(report.IsFailed);
        Assert.True((await ReadAsync(client, principal: new ClaimsPrincipal())).IsFailed);
        Assert.True((await ReadAsync(client, "2027-01")).IsFailed);
    }
}
