namespace PeakMediaRemoteMcpServer.Tests.Usage;

/// <summary>
/// Puts a signed-in caller's usage and quota into a known state through the gateway's real stores
/// (<see cref="UsageStore"/>, <see cref="QuotaStore"/>) at the gateway clock's current time — never by
/// writing to the database behind them. Quota overrides are set by an administrator caller.
/// </summary>
internal static class UsageSeed
{
    public const string Model = "google/gemini-3.8-flash-lite-tts";

    public static readonly Caller Admin = new("00uadministrator-seed", "ada@example.com", IsAdmin: true);

    /// <summary>The OIDC subject the gateway recorded for <paramref name="email"/> at sign-in.</summary>
    public static async Task<string> SubjectOfAsync(GatewayTestHost host, string email = "pat@example.com")
    {
        var opened = await host.Services.GetRequiredService<SqliteConnectionFactory>().OpenAsync();
        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT subject FROM users WHERE email = $email;";
        command.Parameters.AddWithValue("$email", email);
        var subject = await command.ExecuteScalarAsync() as string;
        Assert.False(string.IsNullOrEmpty(subject), $"{email} has not signed in");
        return subject!;
    }

    /// <summary>
    /// Records <paramref name="clips"/> synthesized clips costing <paramref name="totalUsd"/> in total
    /// (split exactly) and <paramref name="cacheHits"/> free cache hits for <paramref name="email"/>.
    /// </summary>
    public static async Task SpendAsync(GatewayTestHost host, decimal totalUsd, int clips = 1, int cacheHits = 0, string email = "pat@example.com")
    {
        var subject = await SubjectOfAsync(host, email);
        var caller = new Caller(subject, email, IsAdmin: false);
        var store = host.Services.GetRequiredService<UsageStore>();
        var totalNano = NanoUsd.From(totalUsd);
        var eachNano = clips == 0 ? 0 : totalNano / clips;
        for (var i = 0; i < clips; i++)
        {
            var nano = i == clips - 1 ? totalNano - (eachNano * (clips - 1)) : eachNano;
            var recorded = await store.RecordAsync(caller, subject, new UsageEntry(Model, "Rasalgethi", 40, NanoUsd.ToUsd(nano), $"seed-{i}.wav", CacheHit: false), CancellationToken.None);
            Assert.True(recorded.IsSuccess);
        }

        for (var i = 0; i < cacheHits; i++)
        {
            var recorded = await store.RecordAsync(caller, subject, new UsageEntry(Model, "Rasalgethi", 40, 0m, $"seed-hit-{i}.wav", CacheHit: true), CancellationToken.None);
            Assert.True(recorded.IsSuccess);
        }
    }

    /// <summary>An administrator sets <paramref name="email"/>'s monthly quota through the quota store.</summary>
    public static async Task SetQuotaAsync(GatewayTestHost host, decimal monthlyUsd, string email = "pat@example.com")
    {
        var subject = await SubjectOfAsync(host, email);
        var set = await host.Services.GetRequiredService<QuotaStore>().SetOverrideAsync(Admin, subject, monthlyUsd);
        Assert.True(set.IsSuccess);
    }
}
