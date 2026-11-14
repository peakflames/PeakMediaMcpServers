namespace PeakMediaRemoteMcpServer.Tests.Dashboard;

public class CacheStatsReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private const long GiB = 1024L * 1024 * 1024;

    private static ClaimsPrincipal AdaSession()
    {
        return DashboardAuthSetup.SessionPrincipal("00uada-admin", "ada@example.com", isAdmin: true);
    }

    private static Task<NarrationClient> StartAsync()
    {
        return NarrationClient.StartAsync(now: Now, overrides: new Dictionary<string, string?> { ["Storage:CacheMaxGb"] = "20" });
    }

    private static Task<FluentResults.Result<CacheStats>> ReadAsync(NarrationClient client, ClaimsPrincipal? principal = null)
    {
        return client.Host.Services.GetRequiredService<CacheStatsReader>().GetAsync(principal ?? AdaSession(), CancellationToken.None);
    }

    /// <summary>Inserts <paramref name="clips"/> cache index rows summing <paramref name="totalBytes"/> (test data only, no files).</summary>
    internal static async Task SeedCacheAsync(GatewayTestHost host, int clips, long totalBytes)
    {
        var opened = await host.Services.GetRequiredService<SqliteConnectionFactory>().OpenAsync();
        await using var connection = opened.Value;
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var each = totalBytes / clips;
        for (var i = 0; i < clips; i++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO audio_cache (cache_key, size_bytes, duration_ms, created_by_subject, created_utc, last_used_utc) VALUES ($key, $size, 1000, 'seed', 0, 0);";
            command.Parameters.AddWithValue("$key", $"seed-cache-{i}.wav");
            command.Parameters.AddWithValue("$size", i == clips - 1 ? totalBytes - (each * (clips - 1)) : each);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    internal static async Task SeedEvictionsAsync(GatewayTestHost host, int count, DateTimeOffset at)
    {
        var opened = await host.Services.GetRequiredService<SqliteConnectionFactory>().OpenAsync();
        await using var connection = opened.Value;
        for (var i = 0; i < count; i++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO cache_evictions (cache_key, at_utc, size_bytes) VALUES ($key, $at, 1);";
            command.Parameters.AddWithValue("$key", $"evicted-{i}.wav");
            command.Parameters.AddWithValue("$at", at.ToUnixTimeMilliseconds());
            await command.ExecuteNonQueryAsync();
        }
    }

    // TOR-08-VHkMu93: 12.4 GB in 5,210 clips under a 20 GB cap, 62% hit rate and 37 evictions this month.
    [Fact]
    public async Task TOR_08_VHkMu93_cache_figures()
    {
        await using var client = await StartAsync();
        var host = client.Host;
        await SeedCacheAsync(host, 5210, (long)Math.Round(12.4 * GiB));
        await UsageSeed.SpendAsync(host, 1.00m, clips: 38, cacheHits: 62);
        await SeedEvictionsAsync(host, 37, Now.AddDays(-1));
        await SeedEvictionsAsync(host, 5, Now.AddMonths(-1));

        var stats = await ReadAsync(client);

        Assert.True(stats.IsSuccess);
        Assert.Equal((long)Math.Round(12.4 * GiB), stats.Value.SizeBytes);
        Assert.Equal(20 * GiB, stats.Value.CapBytes);
        Assert.Equal(5210, stats.Value.Clips);
        Assert.Equal(62, stats.Value.HitRatePercent);
        Assert.Equal(37, stats.Value.EvictedThisMonth);
    }

    // TOR-08-5ZEXDBu: an empty cache reads as 0 clips and 0 bytes.
    [Fact]
    public async Task TOR_08_5ZEXDBu_empty_cache_has_no_clips()
    {
        await using var client = await StartAsync();

        var stats = await ReadAsync(client);

        Assert.True(stats.IsSuccess);
        Assert.Equal(0, stats.Value.Clips);
        Assert.Equal(0, stats.Value.SizeBytes);
        Assert.Equal(0, stats.Value.HitRatePercent);
    }

    // TOR-08-NeOMGCI: with the test fault switch set, the read fails (the screen's error state).
    [Fact]
    public async Task TOR_08_NeOMGCI_fault_switch_fails_the_read()
    {
        await using var client = await StartAsync();
        var factory = client.Host.Services.GetRequiredService<SqliteConnectionFactory>();
        factory.FaultSwitch = new StorageFaultSwitch(true, TimeSpan.Zero);

        Assert.True((await ReadAsync(client)).IsFailed);

        factory.FaultSwitch = StorageFaultSwitch.None;
        Assert.True((await ReadAsync(client)).IsSuccess);
    }

    // Access rule: the cache figures cover every user, so a person without the admin role is refused.
    [Fact]
    public async Task Non_admin_is_refused()
    {
        await using var client = await StartAsync();
        var pat = DashboardAuthSetup.SessionPrincipal("00upat", "pat@example.com", isAdmin: false);

        Assert.True((await ReadAsync(client, pat)).IsFailed);
        Assert.True((await ReadAsync(client, new ClaimsPrincipal())).IsFailed);
    }
}
