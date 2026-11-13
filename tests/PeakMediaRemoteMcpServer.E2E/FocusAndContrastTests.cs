namespace PeakMediaRemoteMcpServer.E2E;

[Collection(DashboardCollection.Name)]
public sealed class FocusAndContrastTests(BrowserFixture browser)
{
    private const string PatEmail = "pat@example.com";

    // Tags every visible focusable control and records its unfocused outline and box-shadow.
    private const string RecordUnfocusedScript = """
        () => {
            const selector = 'a[href], button:not([disabled]), select:not([disabled]), input:not([type=hidden]):not([disabled]), [tabindex]:not([tabindex="-1"])';
            const controls = [...document.querySelectorAll(selector)].filter(e => e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden');
            controls.forEach((e, i) => {
                const s = getComputedStyle(e);
                e.dataset.focusId = String(i);
                e.dataset.unfocused = s.outlineStyle + '|' + s.outlineWidth + '|' + s.outlineColor + '|' + s.boxShadow;
            });
            return controls.length;
        }
        """;

    // The focused control: its focused style vs. unfocused, its box, the viewport and the app header's box.
    private const string FocusedScript = """
        () => {
            const e = document.activeElement;
            if (!e || e === document.body || e.dataset.focusId === undefined) { return null; }
            const s = getComputedStyle(e);
            const r = e.getBoundingClientRect();
            const header = document.querySelector('header[data-app-header]');
            const h = header.getBoundingClientRect();
            return {
                id: e.dataset.focusId,
                name: (e.innerText || e.getAttribute('aria-label') || e.id || e.tagName).trim(),
                changed: (s.outlineStyle + '|' + s.outlineWidth + '|' + s.outlineColor + '|' + s.boxShadow) !== e.dataset.unfocused
                    && (s.outlineStyle !== 'none' || s.boxShadow !== 'none'),
                inHeader: header.contains(e),
                top: r.top, left: r.left, bottom: r.bottom, right: r.right,
                vw: window.innerWidth, vh: window.innerHeight,
                hTop: h.top, hLeft: h.left, hBottom: h.bottom, hRight: h.right
            };
        }
        """;

    [Fact]
    public async Task TOR_09_qu5yesw_every_focused_control_shows_a_focus_indicator_and_is_not_hidden_behind_the_header()
    {
        // TOR-09-qu5yesw (supersedes TOR-09-fjQhX70, CR-sziqlY7)
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync(width: 1024, height: 480);
        await host.SignInAsync(page, StubUser.For(PatEmail));
        await host.SpendAsync(PatEmail, 3.10m, clips: 2);
        await page.ReloadAsync();
        await Assertions.Expect(MyUsageTests.StatValue(page, "Spent")).ToHaveTextAsync("$3.10");

        var controls = await page.EvaluateAsync<int>(RecordUnfocusedScript);
        Assert.True(controls >= 4, $"expected the page's controls, found {controls}");
        await page.EvaluateAsync("() => { document.activeElement?.blur(); window.scrollTo(0, document.documentElement.scrollHeight); }");

        var seen = new HashSet<string>();
        for (var i = 0; i < controls * 2 && seen.Count < controls; i++)
        {
            await page.Keyboard.PressAsync("Shift+Tab");
            var focused = await page.EvaluateAsync<FocusedControl?>(FocusedScript);
            if (focused is null)
            {
                continue;
            }

            seen.Add(focused.Id);
            Assert.True(focused.Changed, $"'{focused.Name}' shows no focus indicator");
            Assert.True(
                focused.Top >= 0 && focused.Left >= 0 && focused.Bottom <= focused.Vh && focused.Right <= focused.Vw,
                $"'{focused.Name}' is outside the viewport");
            if (!focused.InHeader)
            {
                var overlaps = focused.Left < focused.HRight && focused.Right > focused.HLeft && focused.Top < focused.HBottom && focused.Bottom > focused.HTop;
                Assert.False(overlaps, $"'{focused.Name}' lies under the app header");
            }
        }

        Assert.Equal(controls, seen.Count);
        await page.Context.CloseAsync();
    }

    // Every visible element with its own text: its color over the first opaque background behind it, as sRGB via a canvas.
    private const string ContrastScript = """
        () => {
            const ctx = document.createElement('canvas').getContext('2d', { willReadFrequently: true });
            const parse = c => {
                // Any CSS color (oklch included) → sRGB bytes and alpha, through a 1×1 canvas.
                ctx.clearRect(0, 0, 1, 1); ctx.fillStyle = c; ctx.fillRect(0, 0, 1, 1);
                const d = ctx.getImageData(0, 0, 1, 1).data; return [d[0], d[1], d[2], d[3] / 255];
            };
            const background = el => {
                const layers = [];
                for (let n = el; n; n = n.parentElement) {
                    const c = parse(getComputedStyle(n).backgroundColor);
                    if (c[3] > 0) { layers.push(c); if (c[3] >= 1) { break; } }
                }
                let base = [255, 255, 255];
                for (let i = layers.length - 1; i >= 0; i--) {
                    const [r, g, b, a] = layers[i];
                    base = [r * a + base[0] * (1 - a), g * a + base[1] * (1 - a), b * a + base[2] * (1 - a)];
                }
                return base;
            };
            const lum = ([r, g, b]) => { const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }; return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b); };
            const out = [];
            for (const el of document.querySelectorAll('body *')) {
                const own = [...el.childNodes].some(n => n.nodeType === 3 && n.textContent.trim().length > 0);
                if (!own || el.getClientRects().length === 0) { continue; }
                const s = getComputedStyle(el);
                if (s.visibility === 'hidden' || s.display === 'none' || ['SCRIPT', 'STYLE', 'OPTION'].includes(el.tagName)) { continue; }
                const bg = background(el);
                const [r, g, b, a] = parse(s.color);
                const fg = [r * a + bg[0] * (1 - a), g * a + bg[1] * (1 - a), b * a + bg[2] * (1 - a)];
                const l1 = lum(fg), l2 = lum(bg);
                const ratio = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
                const size = parseFloat(s.fontSize), bold = parseInt(s.fontWeight, 10) >= 700;
                const large = size >= 24 || (bold && size >= 18.66);
                out.push({ text: el.textContent.trim().slice(0, 40), ratio, large });
            }
            return out;
        }
        """;

    [Fact]
    public async Task TOR_09_BLJrVJ6_text_contrast_meets_the_minimum_in_light_and_dark()
    {
        // TOR-09-BLJrVJ6
        await using var host = await DashboardHost.StartAsync();
        var page = await browser.NewPageAsync();
        await host.SignInAsync(page, StubUser.For(PatEmail));
        await host.SpendAsync(PatEmail, 3.10m, clips: 2, cacheHits: 1);
        await page.ReloadAsync();
        await DashboardHost.WaitInteractiveAsync(page);

        foreach (var theme in new[] { "light", "dark" })
        {
            await page.GetByLabel("Theme").SelectOptionAsync(theme);
            await Assertions.Expect(MyUsageTests.StatValue(page, "Spent")).ToHaveTextAsync("$3.10");
            var measured = await page.EvaluateAsync<TextContrast[]>(ContrastScript);
            Assert.True(measured.Length >= 15, $"{theme}: measured only {measured.Length} text elements");
            foreach (var text in measured)
            {
                var minimum = text.Large ? 3.0 : 4.5;
                Assert.True(text.Ratio >= minimum, $"{theme}: '{text.Text}' has contrast {text.Ratio:0.00}:1 (needs {minimum}:1)");
            }
        }

        await page.Context.CloseAsync();
    }

    private sealed class FocusedControl
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public bool Changed { get; set; }

        public bool InHeader { get; set; }

        public double Top { get; set; }

        public double Left { get; set; }

        public double Bottom { get; set; }

        public double Right { get; set; }

        public double Vw { get; set; }

        public double Vh { get; set; }

        public double HTop { get; set; }

        public double HLeft { get; set; }

        public double HBottom { get; set; }

        public double HRight { get; set; }
    }

    private sealed class TextContrast
    {
        public string Text { get; set; } = string.Empty;

        public double Ratio { get; set; }

        public bool Large { get; set; }
    }
}
