namespace PeakMediaRemoteMcpServer.E2E;

/// <summary>User Detail (S-03) in a real browser against the real gateway (stub OIDC issuer, real SQLite).</summary>
[Collection(DashboardCollection.Name)]
public sealed class UserDetailTests(BrowserFixture browser)
{
    private const string PatEmail = "pat@example.com";

    private const string SettledState = "main [data-state='populated'], main [data-state='empty'], main [data-state='error']";

    private static ILocator Requests(IPage page)
    {
        return page.Locator("main [data-requests-table]");
    }

    private static ILocator QuotaInput(IPage page)
    {
        return page.GetByLabel("Monthly quota (USD)", new() { Exact = true });
    }

    private static ILocator SaveButton(IPage page)
    {
        return page.Locator("main").GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
    }

    private static ILocator UseDefaultTrigger(IPage page)
    {
        return page.Locator("main").GetByRole(AriaRole.Button, new() { Name = "Use default…", Exact = true });
    }

    private static ILocator Dialog(IPage page)
    {
        return page.GetByRole(AriaRole.Alertdialog);
    }

    /// <summary>Signs the administrator in, then opens User Detail for <paramref name="email"/> and waits for the load to settle.</summary>
    internal static async Task OpenAsync(DashboardHost host, IPage page, string email = PatEmail, string? month = null, bool signIn = true)
    {
        if (signIn)
        {
            await host.SignInAsAdminAsync(page);
        }

        var query = month is null ? string.Empty : $"?month={month}";
        await page.GotoAsync($"{host.BaseUrl}/admin/users/{Uri.EscapeDataString(email)}{query}");
        await Assertions.Expect(page.Locator(SettledState)).ToBeVisibleAsync();
    }

    /// <summary>The quota pat's my_usage reports: the status reader behind the my_usage tool, formatted as the tool formats it.</summary>
    internal static async Task<string> QuotaShownByMyUsageAsync(DashboardHost host, string email = PatEmail)
    {
        var subject = StubUser.For(email).Subject;
        var status = await host.Services.GetRequiredService<UsageStatusReader>()
            .ReadAsync(new Caller(subject, email, IsAdmin: false), UsageMonth.Current(host.Clock), CancellationToken.None);
        Assert.True(status.IsSuccess);
        return UsageText.Dollars(status.Value.Quota);
    }

    [Fact]
    public async Task TOR_08_sfJ3IWh_email_link_on_usage_opens_user_detail_for_the_month()
    {
        // TOR-08-sfJ3IWh
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        await host.SpendAsync(PatEmail, 1.20m, clips: 2);
        var page = await browser.NewPageAsync();
        await host.SignInAsAdminAsync(page);
        await page.GotoAsync(host.BaseUrl + "/admin");
        await Assertions.Expect(page.Locator("main [data-users-table]")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#month-select")).ToHaveValueAsync("2026-11");

        await page.Locator("main [data-users-table]").GetByRole(AriaRole.Link, new() { Name = PatEmail }).ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = PatEmail, Level = 1 })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#month-select")).ToHaveValueAsync("2026-11");
        await Assertions.Expect(Requests(page)).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_wX5QWUB_back_to_usage_and_breadcrumb_return_to_usage()
    {
        // TOR-08-wX5QWUB — pat has no usage in 2026-09
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        var page = await browser.NewPageAsync();
        await OpenAsync(host, page, month: "2026-09");
        await Assertions.Expect(page.Locator("main").GetByText("No usage for pat@example.com in 2026-09")).ToBeVisibleAsync();

        await page.Locator("main").GetByRole(AriaRole.Link, new() { Name = "Back to Usage" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Usage", Exact = true, Level = 1 })).ToBeVisibleAsync();

        await OpenAsync(host, page, month: "2026-09", signIn: false);
        await page.Locator("main").GetByRole(AriaRole.Link, new() { Name = "Usage", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Usage", Exact = true, Level = 1 })).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_JeFRxDI_one_row_per_clip_newest_first_without_the_text()
    {
        // TOR-08-JeFRxDI — a synthesized clip, then the same clip again as a cache hit a minute later.
        // The usage log never receives the narration text (the gateway path is covered by UserDetailServiceTests).
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        await host.SpendAsync(PatEmail, 0.0031m, clips: 1);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.SpendAsync(PatEmail, 0m, clips: 0, cacheHits: 1);
        var page = await browser.NewPageAsync();
        await OpenAsync(host, page, month: "2026-11");

        await Assertions.Expect(Requests(page).Locator("thead th")).ToHaveTextAsync(["Time (UTC)", "Model", "Voice", "Characters", "Cost", "Source"]);
        var rows = Requests(page).Locator("tbody tr");
        await Assertions.Expect(rows).ToHaveCountAsync(2);
        await Assertions.Expect(rows.Nth(0).Locator("[data-source]")).ToHaveTextAsync("cached");
        await Assertions.Expect(rows.Nth(0).Locator("[data-cost]")).ToHaveTextAsync("$0.00");
        await Assertions.Expect(rows.Nth(1).Locator("[data-source]")).ToHaveTextAsync("synthesized");
        Assert.DoesNotContain("Zebra-quartz-lantern-7391", await page.ContentAsync(), StringComparison.Ordinal);
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_i1syL2k_subtitle_summarizes_the_month()
    {
        // TOR-08-i1syL2k — $25.32 of the default $25.00 on 300 synthesized clips and 100 cache hits.
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        await host.SpendAsync(PatEmail, 25.32m, clips: 300, cacheHits: 100);
        var page = await browser.NewPageAsync();
        await OpenAsync(host, page, month: "2026-11");

        var subtitle = page.Locator("main [data-subtitle]");
        foreach (var part in new[] { "2026-11", "$25.32 of $25.00", "OVER", "400 clips", "25% cache hits" })
        {
            await Assertions.Expect(subtitle).ToContainTextAsync(part);
        }

        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_iUG0WAC_current_quota_and_the_default()
    {
        // TOR-08-iUG0WAC — no override for pat.
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        await host.SpendAsync(PatEmail, 1.00m);
        var page = await browser.NewPageAsync();
        await OpenAsync(host, page);

        await Assertions.Expect(QuotaInput(page)).ToHaveValueAsync("25.00");
        await Assertions.Expect(page.Locator("main [data-quota-form]")).ToContainTextAsync("the default");
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_MCIZVXn_month_select_shows_that_months_rows()
    {
        // TOR-08-MCIZVXn — 2 rows in 2026-10, 5 in 2026-11.
        await using var host = await DashboardHost.StartAsync(now: new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));
        await host.RegisterUserAsync(PatEmail);
        await host.SpendAsync(PatEmail, 0.02m, clips: 2);
        host.Clock.SetUtcNow(DashboardHost.DefaultNow);
        await host.SpendAsync(PatEmail, 0.05m, clips: 5);
        var page = await browser.NewPageAsync();
        await OpenAsync(host, page);
        await Assertions.Expect(Requests(page).Locator("tbody tr")).ToHaveCountAsync(5);

        await page.Locator("#month-select").SelectOptionAsync("2026-10");

        await Assertions.Expect(Requests(page).Locator("tbody tr")).ToHaveCountAsync(2);
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_QNNE1fW_save_sets_the_quota_and_confirms_in_text()
    {
        // TOR-08-QNNE1fW
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        var page = await browser.NewPageAsync();
        await OpenAsync(host, page);

        await QuotaInput(page).FillAsync("60");
        await SaveButton(page).ClickAsync();

        await Assertions.Expect(page.Locator("main").GetByText("Quota set to $60.00/month (was $25.00, the default). Applies to Pat's next call.")).ToBeVisibleAsync();
        Assert.Equal("$60.00", await QuotaShownByMyUsageAsync(host));
        await Assertions.Expect(page.Locator("main [data-current-quota]")).ToContainTextAsync("$60.00/month (set by an administrator)");

        // The page shows the stored quota even when nothing can be read afterwards.
        host.SetStorageFault(new StorageFaultSwitch(fail: true, TimeSpan.Zero));
        await UseDefaultTrigger(page).ClickAsync();
        await Assertions.Expect(Dialog(page)).ToContainTextAsync("Their quota of $60.00/month is removed");
        host.SetStorageFault(StorageFaultSwitch.None);
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_bjHma3b_save_failure_is_reported_and_the_amount_kept()
    {
        // TOR-08-bjHma3b
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        var page = await browser.NewPageAsync();
        await OpenAsync(host, page);
        host.SetStorageFault(new StorageFaultSwitch(fail: true, TimeSpan.Zero));

        await QuotaInput(page).FillAsync("60");
        await SaveButton(page).ClickAsync();

        await Assertions.Expect(page.Locator("main").GetByText("Couldn't save the quota. Check your connection and press Save again.")).ToBeVisibleAsync();
        await Assertions.Expect(QuotaInput(page)).ToHaveValueAsync("60");
        host.SetStorageFault(StorageFaultSwitch.None);
        Assert.Equal("$25.00", await QuotaShownByMyUsageAsync(host));
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_09_RA5qSsd_invalid_amount_is_named_next_to_the_field_and_focused()
    {
        // TOR-09-RA5qSsd
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        var page = await browser.NewPageAsync();
        await OpenAsync(host, page);

        await QuotaInput(page).FillAsync("-5");
        await SaveButton(page).ClickAsync();

        await Assertions.Expect(QuotaInput(page)).ToHaveAttributeAsync("aria-invalid", "true");
        var describedBy = await QuotaInput(page).GetAttributeAsync("aria-describedby");
        Assert.False(string.IsNullOrEmpty(describedBy));
        await Assertions.Expect(page.Locator("#" + describedBy)).ToContainTextAsync("Enter an amount of $0.00 or more, for example 60.");
        await Assertions.Expect(QuotaInput(page)).ToBeFocusedAsync();
        Assert.Equal("$25.00", await QuotaShownByMyUsageAsync(host));
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_09_TpkiHjX_saving_shows_progress_within_one_second_and_disables_save()
    {
        // TOR-09-TpkiHjX — the data source is delayed by 3 seconds.
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        var page = await browser.NewPageAsync();
        await OpenAsync(host, page);
        host.SetStorageFault(new StorageFaultSwitch(fail: false, TimeSpan.FromSeconds(3)));

        await QuotaInput(page).FillAsync("60");
        await SaveButton(page).ClickAsync();

        await Assertions.Expect(page.Locator("main").GetByRole(AriaRole.Status).Filter(new() { HasText = "Saving…" })).ToBeVisibleAsync(new() { Timeout = 1000 });
        await Assertions.Expect(SaveButton(page)).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("main").GetByText("Quota set to $60.00/month")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(SaveButton(page)).ToBeEnabledAsync();
        host.SetStorageFault(StorageFaultSwitch.None);
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_09_pQcPvui_fields_have_associated_labels()
    {
        // TOR-09-pQcPvui
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        var page = await browser.NewPageAsync();
        await OpenAsync(host, page);

        await Assertions.Expect(page.GetByRole(AriaRole.Textbox, new() { Name = "Monthly quota (USD)", Exact = true })).ToHaveIdAsync("quota-input");
        await Assertions.Expect(page.GetByRole(AriaRole.Combobox, new() { Name = "Month", Exact = true })).ToHaveIdAsync("month-select");

        await page.Locator("label[for='quota-input']").ClickAsync();

        await Assertions.Expect(QuotaInput(page)).ToBeFocusedAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_6Vzjnrg_use_default_clears_the_override()
    {
        // TOR-08-6Vzjnrg — pat's override is $60.00.
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        await host.SetQuotaAsync(PatEmail, 60m);
        var page = await browser.NewPageAsync();
        await OpenAsync(host, page);

        await UseDefaultTrigger(page).ClickAsync();
        await Dialog(page).GetByRole(AriaRole.Button, new() { Name = "Use default", Exact = true }).ClickAsync();

        await Assertions.Expect(page.Locator("main").GetByText("Quota reset to the default ($25.00/month).")).ToBeVisibleAsync();
        Assert.Equal("$25.00", await QuotaShownByMyUsageAsync(host));
        await Assertions.Expect(QuotaInput(page)).ToHaveValueAsync("25.00");
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_09_do58nOS_confirmation_defaults_to_cancel_and_escape_cancels()
    {
        // TOR-09-do58nOS — pat's override is $60.00.
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        await host.SetQuotaAsync(PatEmail, 60m);
        var page = await browser.NewPageAsync();
        await OpenAsync(host, page);

        await UseDefaultTrigger(page).ClickAsync();

        await Assertions.Expect(Dialog(page)).ToBeVisibleAsync();
        await Assertions.Expect(Dialog(page)).ToContainTextAsync("Reset pat@example.com to the default quota?");
        await Assertions.Expect(Dialog(page)).ToContainTextAsync("Their quota of $60.00/month is removed and the default of $25.00/month applies from their next call.");
        var first = Dialog(page).GetByRole(AriaRole.Button).First;
        await Assertions.Expect(first).ToHaveTextAsync("Cancel");
        await Assertions.Expect(first).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(Dialog(page)).Not.ToBeVisibleAsync();
        Assert.Equal("$60.00", await QuotaShownByMyUsageAsync(host));
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_09_YFn4LMC_dialog_takes_keeps_and_returns_focus()
    {
        // TOR-09-YFn4LMC — pat's override is $60.00; keyboard only.
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        await host.SetQuotaAsync(PatEmail, 60m);
        var page = await browser.NewPageAsync();
        await OpenAsync(host, page);
        await UseDefaultTrigger(page).FocusAsync();

        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(Dialog(page)).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => document.activeElement && document.activeElement.closest('[role=alertdialog]') !== null");
        for (var i = 0; i < 4; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            Assert.True(await page.EvaluateAsync<bool>("() => document.activeElement.closest('[role=alertdialog]') !== null"), $"focus left the dialog after Tab {i + 1}");
        }

        await page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(Dialog(page)).Not.ToBeVisibleAsync();
        await Assertions.Expect(UseDefaultTrigger(page)).ToBeFocusedAsync();
        await page.Context.CloseAsync();
    }

    // The dialog's elapsed time from activation to fully visible, and the longest animation/transition on the
    // dialog, its overlay and the portal layers that hold them.
    private const string OpenDialogScript = """
        async () => {
            const trigger = document.getElementById('use-default-button');
            const start = performance.now();
            trigger.click();
            const visible = () => {
                const d = document.querySelector('[role=alertdialog]');
                if (!d) { return null; }
                const r = d.getBoundingClientRect();
                const s = getComputedStyle(d);
                return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && parseFloat(s.opacity) === 1 && d.getAnimations({ subtree: true }).every(a => a.playState !== 'running') ? d : null;
            };
            let dialog = null;
            while (!(dialog = visible())) {
                if (performance.now() - start > 5000) { return { elapsed: -1, durations: [] }; }
                await new Promise(r => setTimeout(r, 1));
            }
            const elapsed = performance.now() - start;
            const seconds = v => Math.max(...v.split(',').map(x => x.trim().endsWith('ms') ? parseFloat(x) / 1000 : parseFloat(x)));
            const layers = [dialog];
            for (let n = dialog.parentElement; n && n !== document.body; n = n.parentElement) { layers.push(n); }
            const overlays = [...document.querySelectorAll('body *')].filter(e => {
                const s = getComputedStyle(e);
                return s.position === 'fixed' && !e.closest('[data-app-header]') && e.getBoundingClientRect().width >= window.innerWidth;
            });
            const durations = [...new Set([...layers, ...overlays])].map(e => {
                const s = getComputedStyle(e);
                return { name: e.tagName + '.' + (e.className || '').toString().slice(0, 40), animation: seconds(s.animationDuration), transition: seconds(s.transitionDuration) };
            });
            return { elapsed, durations, overlayCount: overlays.length };
        }
        """;

    private sealed class Opened
    {
        public double Elapsed { get; set; }

        public int OverlayCount { get; set; }

        public Duration[] Durations { get; set; } = [];
    }

    private sealed class Duration
    {
        public string Name { get; set; } = string.Empty;

        public double Animation { get; set; }

        public double Transition { get; set; }
    }

    [Fact]
    public async Task TOR_09_GFQ80Xs_reduced_motion_opens_the_dialog_instantly()
    {
        // TOR-09-GFQ80Xs — prefers-reduced-motion: reduce; pat's override is $60.00.
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        await host.SetQuotaAsync(PatEmail, 60m);
        var page = await browser.NewPageAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await OpenAsync(host, page);

        // Warm the circuit's round trip once (open and cancel), as a person's earlier clicks would.
        await UseDefaultTrigger(page).ClickAsync();
        await Assertions.Expect(Dialog(page)).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => document.activeElement && document.activeElement.closest('[role=alertdialog]') !== null");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Dialog(page)).Not.ToBeVisibleAsync();
        await Assertions.Expect(UseDefaultTrigger(page)).ToBeFocusedAsync();

        var opened = await page.EvaluateAsync<Opened>(OpenDialogScript);

        Assert.True(opened.Elapsed >= 0, "the dialog never became fully visible");
        Assert.True(opened.Elapsed <= 50, $"the dialog took {opened.Elapsed:0} ms to become fully visible");
        Assert.True(opened.OverlayCount >= 1, "no overlay found behind the dialog");
        foreach (var d in opened.Durations)
        {
            Assert.True(d.Animation <= 0.01, $"{d.Name}: animation-duration {d.Animation}s");
            Assert.True(d.Transition <= 0.01, $"{d.Name}: transition-duration {d.Transition}s");
        }

        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_09_dty904j_empty_state_keeps_the_quota_form()
    {
        // TOR-09-dty904j — pat has no usage in 2026-11.
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        var page = await browser.NewPageAsync();
        await OpenAsync(host, page, month: "2026-11");

        var main = page.Locator("main");
        await Assertions.Expect(main.GetByText("No usage for pat@example.com in 2026-11")).ToBeVisibleAsync();
        await Assertions.Expect(main.GetByText("This user requested no narration in this month. You can still change their quota above.")).ToBeVisibleAsync();
        await Assertions.Expect(QuotaInput(page)).ToBeVisibleAsync();
        await Assertions.Expect(SaveButton(page)).ToBeVisibleAsync();
        await Assertions.Expect(main.GetByRole(AriaRole.Link, new() { Name = "Back to Usage" })).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_09_CYbsdon_error_state_with_retry_and_TOR_08_bydYtvm_retry_reloads()
    {
        // TOR-09-CYbsdon, then TOR-08-bydYtvm
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        await host.SpendAsync(PatEmail, 1.00m, clips: 2);
        var page = await browser.NewPageAsync();
        await host.SignInAsAdminAsync(page);
        host.SetStorageFault(new StorageFaultSwitch(fail: true, TimeSpan.Zero));

        await page.GotoAsync($"{host.BaseUrl}/admin/users/{Uri.EscapeDataString(PatEmail)}");

        var main = page.Locator("main");
        await Assertions.Expect(main.GetByText("Couldn't load usage for pat@example.com. Check your connection and try again.")).ToBeVisibleAsync();
        var retry = main.GetByRole(AriaRole.Button, new() { Name = "Retry" });
        await Assertions.Expect(retry).ToBeVisibleAsync();
        Assert.Equal(0, await Requests(page).CountAsync());

        host.SetStorageFault(StorageFaultSwitch.None);
        await retry.ClickAsync();

        await Assertions.Expect(Requests(page)).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_08_bydYtvm_retry_after_a_failed_load_shows_the_requests_table()
    {
        // TOR-08-bydYtvm — the failed load is shown first, then Retry with the fault cleared.
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        await host.SpendAsync(PatEmail, 1.00m, clips: 1);
        var page = await browser.NewPageAsync();
        await host.SignInAsAdminAsync(page);
        host.SetStorageFault(new StorageFaultSwitch(fail: true, TimeSpan.Zero));
        await page.GotoAsync($"{host.BaseUrl}/admin/users/{Uri.EscapeDataString(PatEmail)}");
        await Assertions.Expect(page.Locator("main [data-state='error']")).ToBeVisibleAsync();

        host.SetStorageFault(StorageFaultSwitch.None);
        await page.Locator("main").GetByRole(AriaRole.Button, new() { Name = "Retry" }).ClickAsync();

        await Assertions.Expect(Requests(page)).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_09_9irho1R_titles_TOR_09_hdlqc7J_single_h1_and_TOR_09_PVdyo7H_current_nav_item()
    {
        // TOR-09-9irho1R, TOR-09-hdlqc7J, TOR-09-PVdyo7H — My Usage (S-01), then User Detail (S-03).
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        await host.SpendAsync(PatEmail, 1.00m);
        var page = await browser.NewPageAsync();
        await host.SignInAsAdminAsync(page);
        await DashboardHost.WaitInteractiveAsync(page);
        var nav = page.GetByRole(AriaRole.Navigation, new() { Name = "Primary" });

        Assert.Equal("My usage · PeakMedia Gateway", await page.TitleAsync());
        await Assertions.Expect(page.Locator("h1:visible")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("h1:visible")).ToHaveTextAsync("My usage");
        await Assertions.Expect(nav.Locator("a[aria-current]")).ToHaveCountAsync(1);
        await Assertions.Expect(nav.Locator("a[aria-current='page']")).ToHaveTextAsync("My usage");

        await OpenAsync(host, page, signIn: false);

        Assert.Equal("pat@example.com · PeakMedia Gateway", await page.TitleAsync());
        await Assertions.Expect(page.Locator("h1:visible")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("h1:visible")).ToHaveTextAsync(PatEmail);
        await Assertions.Expect(nav.Locator("a[aria-current]")).ToHaveCountAsync(1);
        await Assertions.Expect(nav.Locator("a[aria-current]")).ToHaveTextAsync("Usage");
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_09_7aP7tWP_keyboard_reaches_every_control_in_order_and_wraps()
    {
        // TOR-09-7aP7tWP — pointer unused.
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        await host.SpendAsync(PatEmail, 1.00m);
        var page = await browser.NewPageAsync();
        await OpenAsync(host, page);
        await page.EvaluateAsync("() => { document.activeElement?.blur(); window.scrollTo(0, 0); }");

        // Blazor moves focus to the page's h1 after navigation, so collect one full Tab cycle and read it from the
        // first control at the top of the page (the first navigation link).
        var expected = new[] { "nav:My usage", "nav:Usage", "nav:Cache", "Sign out", "crumb:Usage", "month-select", "quota-input", "Save", "Use default…" };
        var reached = new List<string>();
        for (var i = 0; i < 60; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            reached.Add(await page.EvaluateAsync<string>("""
                () => {
                    const e = document.activeElement;
                    if (!e || e === document.body) { return ''; }
                    if (e.closest('nav[aria-label=Primary]')) { return 'nav:' + e.textContent.trim(); }
                    if (e.closest('main nav')) { return 'crumb:' + e.textContent.trim(); }
                    if (e.id) { return e.id === 'use-default-button' ? 'Use default…' : e.id; }
                    return e.textContent.trim() || e.tagName;
                }
                """));
        }

        var start = reached.IndexOf("nav:My usage");
        var again = reached.IndexOf("nav:My usage", start + 1);
        Assert.True(start >= 0 && again > start, $"Tab never wrapped back to the first control: {string.Join(" | ", reached)}");
        var cycle = reached.GetRange(start, again - start);
        var order = expected.Select(e => cycle.IndexOf(e)).ToList();
        Assert.True(order.All(i => i >= 0), $"not reached: {string.Join(", ", expected.Where(e => !cycle.Contains(e)))} — cycle {string.Join(" | ", cycle)}");
        Assert.True(order.SequenceEqual(order.Order()), $"out of order: {string.Join(" | ", cycle)}");
        // After the last page control, focus leaves the page content (browser chrome) and returns to the first control.
        var after = cycle.Skip(cycle.IndexOf("Use default…") + 1).ToList();
        Assert.True(after.All(n => n.Length == 0 || n == "Back to Usage"), $"unexpected stops after Use default…: {string.Join(" | ", after)}");

        // Enter on the focused Save button submits the quota form.
        await QuotaInput(page).FillAsync("40");
        await SaveButton(page).FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("main").GetByText("Quota set to $40.00/month (was $25.00, the default). Applies to Pat's next call.")).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    // Every visible control's box, the page's scroll width and the viewport width.
    private const string LayoutScript = """
        () => {
            const controls = [...document.querySelectorAll('button, a[href], select, input:not([type=hidden])')]
                .filter(e => e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden');
            const boxes = controls.map(e => { const r = e.getBoundingClientRect(); return { name: (e.textContent.trim() || e.id || e.getAttribute('aria-label') || e.tagName).slice(0, 30), left: r.left, right: r.right, top: r.top + window.scrollY, bottom: r.bottom + window.scrollY }; });
            return { scrollWidth: document.documentElement.scrollWidth, viewport: window.innerWidth, boxes };
        }
        """;

    private sealed class Layout
    {
        public double ScrollWidth { get; set; }

        public double Viewport { get; set; }

        public Box[] Boxes { get; set; } = [];
    }

    private sealed class Box
    {
        public string Name { get; set; } = string.Empty;

        public double Left { get; set; }

        public double Right { get; set; }

        public double Top { get; set; }

        public double Bottom { get; set; }
    }

    private static void AssertLayout(Layout layout, string screen, bool checkOverlap)
    {
        Assert.True(layout.ScrollWidth <= layout.Viewport, $"{screen}: scrollWidth {layout.ScrollWidth} > viewport {layout.Viewport}");
        Assert.True(layout.Boxes.Length >= 2, $"{screen}: found only {layout.Boxes.Length} controls");
        foreach (var b in layout.Boxes)
        {
            Assert.True(b.Left >= 0 && b.Right <= layout.Viewport + 0.5, $"{screen}: '{b.Name}' lies outside the viewport ({b.Left:0}–{b.Right:0})");
        }

        if (!checkOverlap)
        {
            return;
        }

        for (var i = 0; i < layout.Boxes.Length; i++)
        {
            for (var j = i + 1; j < layout.Boxes.Length; j++)
            {
                var a = layout.Boxes[i];
                var c = layout.Boxes[j];
                var overlap = a.Left < c.Right - 0.5 && a.Right > c.Left + 0.5 && a.Top < c.Bottom - 0.5 && a.Bottom > c.Top + 0.5;
                Assert.False(overlap, $"{screen}: '{a.Name}' overlaps '{c.Name}'");
            }
        }
    }

    private static async Task SeedWithRowsAsync(DashboardHost host)
    {
        await host.RegisterUserAsync(PatEmail);
        await host.SpendAsync(PatEmail, 25.32m, clips: 3, cacheHits: 1);
    }

    [Fact]
    public async Task TOR_09_cTG2frO_my_usage_and_user_detail_fit_320px()
    {
        // TOR-09-cTG2frO — an administrator at 320 CSS px; pat has usage rows.
        await using var host = await DashboardHost.StartAsync();
        await SeedWithRowsAsync(host);
        await host.SpendAsync("ada@example.com", 1.00m, clips: 1);
        var page = await browser.NewPageAsync(width: 320, height: 800);
        await host.SignInAsAdminAsync(page);
        await DashboardHost.WaitInteractiveAsync(page);
        AssertLayout(await page.EvaluateAsync<Layout>(LayoutScript), "My usage", checkOverlap: true);

        await OpenAsync(host, page, signIn: false);
        await Assertions.Expect(Requests(page)).ToBeVisibleAsync();
        AssertLayout(await page.EvaluateAsync<Layout>(LayoutScript), "User detail", checkOverlap: true);
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task TOR_09_1DjTmDv_my_usage_and_user_detail_at_200_percent_zoom()
    {
        // TOR-09-1DjTmDv — a 1280 CSS px window at 200 % zoom lays out at 640 CSS px with a device scale of 2.
        await using var host = await DashboardHost.StartAsync();
        await SeedWithRowsAsync(host);
        await using var context = await browser.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 640, Height = 400 }, DeviceScaleFactor = 2 });
        context.SetDefaultTimeout(15_000);
        var page = await context.NewPageAsync();
        await host.SignInAsAdminAsync(page);
        await DashboardHost.WaitInteractiveAsync(page);
        AssertLayout(await page.EvaluateAsync<Layout>(LayoutScript), "My usage", checkOverlap: false);

        await OpenAsync(host, page, signIn: false);
        await Assertions.Expect(Requests(page)).ToBeVisibleAsync();
        AssertLayout(await page.EvaluateAsync<Layout>(LayoutScript), "User detail", checkOverlap: false);
        Assert.Equal(string.Empty, await page.EvaluateAsync<string>(ClippedCellsScript));
    }

    // Table cells cut off by a box that hides its overflow, or lying outside the viewport ("" when none).
    private const string ClippedCellsScript = """
        () => {
            const clipped = [...document.querySelectorAll('main *')]
                .filter(e => !e.closest('.sr-only') && getComputedStyle(e).overflowX === 'hidden' && e.scrollWidth > e.clientWidth + 1)
                .map(e => 'box ' + e.className + ' ' + e.scrollWidth + '>' + e.clientWidth);
            const outside = [...document.querySelectorAll('main td, main th')]
                .filter(c => c.getClientRects().length > 0 && c.getBoundingClientRect().right > window.innerWidth + 0.5)
                .map(c => 'cell ' + c.textContent.trim());
            return [...clipped, ...outside].join(' | ');
        }
        """;

    // Border colour of a control against the background behind it, and its focus ring against that background (sRGB via a canvas).
    private const string BorderContrastScript = """
        (selector) => {
            const ctx = document.createElement('canvas').getContext('2d', { willReadFrequently: true });
            const parse = c => { ctx.clearRect(0, 0, 1, 1); ctx.fillStyle = c; ctx.fillRect(0, 0, 1, 1); const d = ctx.getImageData(0, 0, 1, 1).data; return [d[0], d[1], d[2], d[3] / 255]; };
            const behind = el => {
                for (let n = el.parentElement; n; n = n.parentElement) { const c = parse(getComputedStyle(n).backgroundColor); if (c[3] >= 1) { return c; } }
                return [255, 255, 255, 1];
            };
            const lum = ([r, g, b]) => { const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }; return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b); };
            const ratio = (a, b) => { const l1 = lum(a), l2 = lum(b); return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05); };
            const over = (c, bg) => [c[0] * c[3] + bg[0] * (1 - c[3]), c[1] * c[3] + bg[1] * (1 - c[3]), c[2] * c[3] + bg[2] * (1 - c[3])];
            const el = document.querySelector(selector);
            const bg = behind(el);
            const s = getComputedStyle(el);
            // A filled control (the primary Save button) is bounded by its fill; an outlined one by its border.
            const fill = parse(s.backgroundColor);
            const edge = parseFloat(s.borderTopWidth) > 0 && parse(s.borderTopColor)[3] > 0 ? parse(s.borderTopColor) : fill;
            const boundary = ratio(over(edge, bg), bg);
            el.focus({ focusVisible: true });
            const f = getComputedStyle(el);
            // The indicator is whatever focus draws: an outline and/or box-shadow rings (spread > 0). The most visible one counts.
            const marks = [];
            if (f.outlineStyle !== 'none' && parseFloat(f.outlineWidth) > 0) { marks.push(parse(f.outlineColor)); }
            for (const layer of f.boxShadow === 'none' ? [] : f.boxShadow.split(/,(?![^(]*\))/)) {
                const color = layer.match(/^\s*((?:rgba?|oklch|oklab|color|hsla?)\([^)]*\)|#[0-9a-f]+|[a-z]+)/i);
                const lengths = layer.replace(color ? color[0] : '', '').trim().split(/\s+/).map(parseFloat);
                if (color && lengths.length >= 4 && lengths[3] > 0) { marks.push(parse(color[1])); }
            }
            const focus = marks.length === 0 ? 0 : Math.max(...marks.map(m => ratio(over(m, bg), bg)));
            el.blur();
            return { boundary, focus, detail: s.backgroundColor + ' / ' + s.borderTopWidth + ' ' + s.borderTopColor + ' on ' + bg.join(',') };
        }
        """;

    private sealed class Measured
    {
        public double Boundary { get; set; }

        public double Focus { get; set; }

        public string Detail { get; set; } = string.Empty;
    }

    [Fact]
    public async Task TOR_09_P5YL2JO_control_boundaries_and_focus_rings_reach_3_to_1_in_light_and_dark()
    {
        // TOR-09-P5YL2JO
        await using var host = await DashboardHost.StartAsync();
        await host.RegisterUserAsync(PatEmail);
        await host.SpendAsync(PatEmail, 1.00m);
        var page = await browser.NewPageAsync();
        await OpenAsync(host, page);

        foreach (var theme in new[] { "light", "dark" })
        {
            await page.GetByLabel("Theme").SelectOptionAsync(theme);
            await page.WaitForFunctionAsync($"() => document.documentElement.classList.contains('dark') === {(theme == "dark" ? "true" : "false")}");
            foreach (var (name, selector) in new[] { ("Monthly quota (USD)", "#quota-input"), ("Month", "#month-select"), ("Save", "main [data-save]") })
            {
                // Keyboard focus, so :focus-visible applies.
                await page.Locator(selector).FocusAsync();
                await page.Keyboard.PressAsync("Shift+Tab");
                await page.Keyboard.PressAsync("Tab");
                // Colors are measured once the control's color transition has finished.
                await page.WaitForFunctionAsync("() => document.getAnimations().every(a => a.playState !== 'running')");
                var measured = await page.EvaluateAsync<Measured>(BorderContrastScript, selector);
                Assert.True(measured.Boundary >= 3, $"{theme}: {name} boundary {measured.Boundary:0.00}:1 ({measured.Detail})");
                Assert.True(measured.Focus >= 3, $"{theme}: {name} focus indicator {measured.Focus:0.00}:1");
            }
        }

        await page.Context.CloseAsync();
    }
}
