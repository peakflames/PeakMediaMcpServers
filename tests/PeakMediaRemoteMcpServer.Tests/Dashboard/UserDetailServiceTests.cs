namespace PeakMediaRemoteMcpServer.Tests.Dashboard;

/// <summary>User Detail (S-03)'s data and quota actions, through the real stores and the real sign-in.</summary>
public class UserDetailServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private const string Marker = "Zebra-quartz-lantern-7391 is the marker sentence.";

    private static ClaimsPrincipal AdaSession()
    {
        return DashboardAuthSetup.SessionPrincipal("00uada-admin", "ada@example.com", isAdmin: true);
    }

    private static UserDetailService Service(NarrationClient client)
    {
        return client.Host.Services.GetRequiredService<UserDetailService>();
    }

    private static async Task<UserDetailReport> ReadAsync(NarrationClient client, string month = "2026-11")
    {
        var subject = await UsageSeed.SubjectOfAsync(client.Host);
        var report = await Service(client).GetAsync(AdaSession(), subject, month, CancellationToken.None);
        Assert.True(report.IsSuccess, report.IsFailed ? report.Errors[0].Message : null);
        return report.Value;
    }

    private static async Task<string> QuotaShownByMyUsageAsync(NarrationClient client)
    {
        return NarrationClient.UsageTable((await client.MyUsageAsync()).Text)["quota"];
    }

    // TOR-08-JeFRxDI: a clip with the marker text, then the same clip again as a cache hit → newest first
    // (cached $0.00, then synthesized), and no field of the report holds the narration text.
    [Fact]
    public async Task TOR_08_JeFRxDI_one_row_per_clip_newest_first_without_text()
    {
        await using var client = await NarrationClient.StartAsync(now: Now);
        var first = await client.SynthesizeAsync([NarrationClient.Clip(1, Marker)]);
        Assert.DoesNotContain("ERROR", first.Text);
        client.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await client.SynthesizeAsync([NarrationClient.Clip(1, Marker)]);
        Assert.DoesNotContain("ERROR", second.Text);

        var report = await ReadAsync(client);

        Assert.Equal(2, report.Rows.Count);
        Assert.True(report.Rows[0].CacheHit);
        Assert.Equal(0m, report.Rows[0].Cost);
        Assert.False(report.Rows[1].CacheHit);
        Assert.DoesNotContain("Zebra-quartz-lantern-7391", JsonSerializer.Serialize(report), StringComparison.Ordinal);
    }

    // TOR-08-i1syL2k: $25.32 of $25.00 on 300 synthesized clips + 100 cache hits → OVER, 400 clips, 25 % cache hits.
    [Fact]
    public async Task TOR_08_i1syL2k_month_summary()
    {
        await using var client = await NarrationClient.StartAsync(now: Now);
        await UsageSeed.SpendAsync(client.Host, 25.32m, clips: 300, cacheHits: 100);

        var report = await ReadAsync(client);

        Assert.Equal(25.32m, report.Spent);
        Assert.Equal(25.00m, report.Quota);
        Assert.True(report.IsOver);
        Assert.Equal(400, report.Clips);
        Assert.Equal(25, report.CacheHitPercent);
    }

    // TOR-08-MCIZVXn: 2 rows in 2026-10 and 5 in 2026-11 → picking 2026-10 returns exactly 2.
    [Fact]
    public async Task TOR_08_MCIZVXn_rows_of_the_chosen_month()
    {
        await using var client = await NarrationClient.StartAsync(now: new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));
        await UsageSeed.SpendAsync(client.Host, 0.02m, clips: 2);
        client.Clock.SetUtcNow(Now);
        await UsageSeed.SpendAsync(client.Host, 0.05m, clips: 5);

        Assert.Equal(2, (await ReadAsync(client, "2026-10")).Rows.Count);
        Assert.Equal(5, (await ReadAsync(client, "2026-11")).Rows.Count);
    }

    // TOR-08-sfJ3IWh: User Detail reads only for administrators; a non-admin session and an unknown subject fail.
    [Fact]
    public async Task TOR_08_sfJ3IWh_admin_only_and_unknown_subject_fails()
    {
        await using var client = await NarrationClient.StartAsync(now: Now);
        var subject = await UsageSeed.SubjectOfAsync(client.Host);
        var patSession = DashboardAuthSetup.SessionPrincipal(subject, "pat@example.com", isAdmin: false);

        Assert.True((await Service(client).GetAsync(patSession, subject, "2026-11", CancellationToken.None)).IsFailed);
        Assert.True((await Service(client).GetAsync(AdaSession(), "00unobody", "2026-11", CancellationToken.None)).IsFailed);
        Assert.Equal("pat@example.com", (await ReadAsync(client)).Email);

        // The Usage table's link names the person by email; it resolves to the same subject.
        var byEmail = await Service(client).GetAsync(AdaSession(), "pat@example.com", "2026-11", CancellationToken.None);
        Assert.True(byEmail.IsSuccess);
        Assert.Equal(subject, byEmail.Value.Subject);
    }

    // TOR-08-QNNE1fW: "60" over the default → exact confirmation text, and pat's next my_usage shows $60.00.
    [Fact]
    public async Task TOR_08_QNNE1fW_save_confirms_and_applies()
    {
        await using var client = await NarrationClient.StartAsync(now: Now);
        var subject = await UsageSeed.SubjectOfAsync(client.Host);

        var saved = await Service(client).SaveQuotaAsync(AdaSession(), subject, "pat@example.com", "60", CancellationToken.None);

        Assert.Equal(QuotaChangeOutcome.Saved, saved.Outcome);
        Assert.Equal("Quota set to $60.00/month (was $25.00, the default). Applies to Pat's next call.", saved.Message);
        Assert.Equal("$60.00", await QuotaShownByMyUsageAsync(client));

        var again = await Service(client).SaveQuotaAsync(AdaSession(), subject, "pat@example.com", "75", CancellationToken.None);
        Assert.Equal("Quota set to $75.00/month (was $60.00). Applies to Pat's next call.", again.Message);
    }

    // TOR-09-RA5qSsd: "-5" (and unreadable text) → the field error text; the store is not touched.
    [Theory]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task TOR_09_RA5qSsd_invalid_amount_stores_nothing(string typed)
    {
        await using var client = await NarrationClient.StartAsync(now: Now);
        var subject = await UsageSeed.SubjectOfAsync(client.Host);

        var saved = await Service(client).SaveQuotaAsync(AdaSession(), subject, "pat@example.com", typed, CancellationToken.None);

        Assert.Equal(QuotaChangeOutcome.Invalid, saved.Outcome);
        Assert.Equal("Enter an amount of $0.00 or more, for example 60.", saved.Message);
        Assert.Equal("$25.00", await QuotaShownByMyUsageAsync(client));
    }

    // TOR-08-bjHma3b: the fault switch forces the data source to fail → SaveFailed with the exact text; quota unchanged.
    [Fact]
    public async Task TOR_08_bjHma3b_storage_failure_reports_and_keeps_quota()
    {
        await using var client = await NarrationClient.StartAsync(now: Now);
        var subject = await UsageSeed.SubjectOfAsync(client.Host);
        var factory = client.Host.Services.GetRequiredService<SqliteConnectionFactory>();
        factory.FaultSwitch = new StorageFaultSwitch(true, TimeSpan.Zero);

        var saved = await Service(client).SaveQuotaAsync(AdaSession(), subject, "pat@example.com", "60", CancellationToken.None);

        factory.FaultSwitch = StorageFaultSwitch.None;
        Assert.Equal(QuotaChangeOutcome.SaveFailed, saved.Outcome);
        Assert.Equal("Couldn't save the quota. Check your connection and press Save again.", saved.Message);
        Assert.Equal("$25.00", await QuotaShownByMyUsageAsync(client));
    }

    // TOR-08-6Vzjnrg: override $60 → reset → exact text and my_usage shows $25.00.
    [Fact]
    public async Task TOR_08_6Vzjnrg_reset_to_default()
    {
        await using var client = await NarrationClient.StartAsync(now: Now);
        var subject = await UsageSeed.SubjectOfAsync(client.Host);
        await UsageSeed.SetQuotaAsync(client.Host, 60m);

        var reset = await Service(client).ResetQuotaAsync(AdaSession(), subject, CancellationToken.None);

        Assert.Equal(QuotaChangeOutcome.Saved, reset.Outcome);
        Assert.Equal("Quota reset to the default ($25.00/month).", reset.Message);
        Assert.Equal("$25.00", await QuotaShownByMyUsageAsync(client));
    }

    // TOR-08-QNNE1fW: a non-admin session cannot change anyone's quota.
    [Fact]
    public async Task TOR_08_QNNE1fW_non_admin_cannot_save_or_reset()
    {
        await using var client = await NarrationClient.StartAsync(now: Now);
        var subject = await UsageSeed.SubjectOfAsync(client.Host);
        var patSession = DashboardAuthSetup.SessionPrincipal(subject, "pat@example.com", isAdmin: false);

        Assert.Equal(QuotaChangeOutcome.SaveFailed, (await Service(client).SaveQuotaAsync(patSession, subject, "pat@example.com", "1000", CancellationToken.None)).Outcome);
        Assert.Equal(QuotaChangeOutcome.SaveFailed, (await Service(client).ResetQuotaAsync(patSession, subject, CancellationToken.None)).Outcome);
        Assert.Equal("$25.00", await QuotaShownByMyUsageAsync(client));
    }

    [Theory]
    [InlineData("pat@example.com", "Pat")]
    [InlineData("pat.lee@example.com", "Pat")]
    [InlineData("sam_o@example.com", "Sam")]
    public void Display_name_is_the_first_word_of_the_local_part(string email, string expected)
    {
        Assert.Equal(expected, UserDetailService.DisplayName(email));
    }
}
