namespace PeakMediaRemoteMcpServer.E2E;

/// <summary>
/// The real gateway — <see cref="Program.CreateBuilder"/> + <see cref="Program.BuildApp"/>, real Kestrel
/// on a free port, real SQLite in a temp directory, Development environment — with the stub OIDC issuer
/// as the organization's provider. The usage clock is fixed (default 2026-11-12 10:00 UTC) so the TORs'
/// dated Givens hold; usage is seeded through the gateway's own <see cref="UsageStore"/> and
/// <see cref="QuotaStore"/>. People sign in through the real OIDC redirect flow in the browser.
/// </summary>
public sealed class DashboardHost : IAsyncDisposable
{
    public const string AdminGroup = "PeakMedia Admins";

    public static readonly DateTimeOffset DefaultNow = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static readonly Caller SeedAdmin = new("00uadministrator-seed", "ada@example.com", IsAdmin: true);

    private readonly WebApplication _app;
    private readonly TempDirectory _data;

    private DashboardHost(WebApplication app, TempDirectory data, StubOidcIssuer stub, FakeTimeProvider clock, string baseUrl)
    {
        _app = app;
        _data = data;
        Stub = stub;
        Clock = clock;
        BaseUrl = baseUrl;
    }

    public string BaseUrl { get; }

    public StubOidcIssuer Stub { get; }

    public FakeTimeProvider Clock { get; }

    public IServiceProvider Services => _app.Services;

    public static async Task<DashboardHost> StartAsync(DateTimeOffset? now = null)
    {
        var stub = await StubOidcIssuer.StartAsync();
        var data = new TempDirectory();
        var port = FreePort.Next();
        var baseUrl = $"http://127.0.0.1:{port}";
        var clock = new FakeTimeProvider(now ?? DefaultNow);

        var builder = Program.CreateBuilder(["--environment=Development"]);
        builder.WebHost.UseUrls(baseUrl);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["McpAuth:Enabled"] = "true",
            ["McpAuth:Resource"] = $"{baseUrl}/mcp",
            ["Broker:IssuerUri"] = baseUrl,
            ["Broker:UpstreamAuthority"] = stub.Authority,
            ["Broker:UpstreamClientId"] = stub.ClientId,
            ["Broker:UpstreamClientSecret"] = stub.ClientSecret,
            ["Admin:Group"] = AdminGroup,
            ["OpenRouter:ApiKey"] = "test-key",
            ["Storage:DatabasePath"] = data.File("peakmedia.db"),
        });
        builder.Services.AddSingleton<TimeProvider>(clock);
        var app = Program.BuildApp(builder);
        await app.StartAsync();
        return new DashboardHost(app, data, stub, clock, baseUrl);
    }

    /// <summary>The test fault switch (<c>fail</c> / <c>delay:&lt;ms&gt;</c>); <see cref="StorageFaultSwitch.None"/> clears it.</summary>
    public void SetStorageFault(StorageFaultSwitch faultSwitch)
    {
        Services.GetRequiredService<SqliteConnectionFactory>().FaultSwitch = faultSwitch;
    }

    /// <summary>Opens <c>/</c> as <paramref name="user"/>: the browser goes to the stub provider and back, then lands on My Usage.</summary>
    public async Task SignInAsync(IPage page, StubUser user)
    {
        Stub.SetNextUser(user);
        await page.GotoAsync(BaseUrl + "/");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "My usage", Level = 1 })).ToBeVisibleAsync();
    }

    /// <summary>Waits until the interactive circuit is up (the theme choice is read over it).</summary>
    public static async Task WaitInteractiveAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => window.Blazor !== undefined && document.querySelector('[data-state]') !== null");
    }

    /// <summary>
    /// Records <paramref name="clips"/> synthesized clips costing <paramref name="totalUsd"/> and
    /// <paramref name="cacheHits"/> cache hits for <paramref name="email"/>
    /// at the host's clock, through the gateway's usage store.
    /// </summary>
    public async Task SpendAsync(string email, decimal totalUsd, int clips = 1, int cacheHits = 0)
    {
        var subject = StubUser.For(email).Subject;
        var caller = new Caller(subject, email, IsAdmin: false);
        var store = Services.GetRequiredService<UsageStore>();
        var totalNano = NanoUsd.From(totalUsd);
        var eachNano = clips == 0 ? 0 : totalNano / clips;
        for (var i = 0; i < clips; i++)
        {
            var nano = i == clips - 1 ? totalNano - (eachNano * (clips - 1)) : eachNano;
            var recorded = await store.RecordAsync(caller, subject, new UsageEntry("google/gemini-3.8-flash-lite-tts", "Rasalgethi", 40, NanoUsd.ToUsd(nano), $"seed-{i}.wav", CacheHit: false), CancellationToken.None);
            Assert.True(recorded.IsSuccess, recorded.IsFailed ? recorded.Errors[0].Message : null);
        }

        for (var i = 0; i < cacheHits; i++)
        {
            var recorded = await store.RecordAsync(caller, subject, new UsageEntry("google/gemini-3.8-flash-lite-tts", "Rasalgethi", 40, 0m, $"seed-hit-{i}.wav", CacheHit: true), CancellationToken.None);
            Assert.True(recorded.IsSuccess, recorded.IsFailed ? recorded.Errors[0].Message : null);
        }
    }

    /// <summary>An administrator sets <paramref name="email"/>'s monthly quota; the person must have signed in once.</summary>
    public async Task SetQuotaAsync(string email, decimal monthlyUsd)
    {
        var set = await Services.GetRequiredService<QuotaStore>().SetOverrideAsync(SeedAdmin, StubUser.For(email).Subject, monthlyUsd);
        Assert.True(set.IsSuccess, set.IsFailed ? set.Errors[0].Message : null);
    }

    /// <summary>Records a sign-in for <paramref name="email"/> without a browser (another user who exists in the gateway).</summary>
    public async Task RegisterUserAsync(string email)
    {
        var recorded = await Services.GetRequiredService<UserStore>().UpsertSignInAsync(StubUser.For(email).Subject, email, false, DateTimeOffset.UtcNow);
        Assert.True(recorded.IsSuccess);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        await Stub.DisposeAsync();
        _data.Dispose();
    }
}
