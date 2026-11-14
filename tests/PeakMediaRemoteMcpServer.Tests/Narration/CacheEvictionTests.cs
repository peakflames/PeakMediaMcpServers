namespace PeakMediaRemoteMcpServer.Tests.Narration;

public class CacheEvictionTests
{
    // Every test clip has 8 words; the fake answers 150 wpm → 3,200 ms of 24 kHz 16-bit mono PCM + a 44-byte header.
    private const long ClipBytes = (3200 * 48) + WavAudio.HeaderLength;

    private static Task<NarrationClient> StartWithRoomForThreeClipsAsync()
    {
        var maxGb = ClipBytes * 3.5 / (1024d * 1024 * 1024);
        return NarrationClient.StartAsync(overrides: new Dictionary<string, string?>
        {
            ["Storage:CacheMaxGb"] = maxGb.ToString("R", CultureInfo.InvariantCulture),
        });
    }

    private static object Clip(string name)
    {
        return NarrationClient.Clip(1, $"Clip {name} says hello to everyone listening today.");
    }

    /// <summary>Synthesizes one clip a second after the previous call, returning its single entry.</summary>
    private static async Task<JsonElement> SynthesizeOneAsync(NarrationClient client, string name)
    {
        client.Clock.Advance(TimeSpan.FromSeconds(1));
        var result = await client.SynthesizeAsync([Clip(name)]);
        return Assert.Single(NarrationClient.Entries(result.Text));
    }

    private static async Task RunEvictorAsync(NarrationClient client)
    {
        var report = await client.Host.Services.GetRequiredService<CacheEvictor>().RunOnceAsync();
        Assert.True(report.IsSuccess);
    }

    // TOR-04-SGh52Vj: with room for 3 clips, A, B, C then D synthesized and the eviction run, B, C and D are
    // still cached and A (least recently used) is not.
    [Fact]
    public async Task TOR_04_SGh52Vj_oldest_clip_is_evicted_when_over_limit()
    {
        await using var client = await StartWithRoomForThreeClipsAsync();
        var a = await SynthesizeOneAsync(client, "A");
        Assert.Equal(ClipBytes, await SizeOfAsync(client, a.GetProperty("cacheKey").GetString()!));
        await SynthesizeOneAsync(client, "B");
        await SynthesizeOneAsync(client, "C");
        await SynthesizeOneAsync(client, "D");

        await RunEvictorAsync(client);

        client.Clock.Advance(TimeSpan.FromSeconds(1));
        var bcd = await client.SynthesizeAsync([
            NarrationClient.Clip(1, "Clip B says hello to everyone listening today."),
            NarrationClient.Clip(2, "Clip C says hello to everyone listening today."),
            NarrationClient.Clip(3, "Clip D says hello to everyone listening today."),
        ]);
        Assert.All(NarrationClient.Entries(bcd.Text), entry => Assert.True(entry.GetProperty("cached").GetBoolean()));
        var againA = await SynthesizeOneAsync(client, "A");
        Assert.False(againA.GetProperty("cached").GetBoolean());
    }

    // TOR-04-rYFhU7O: A, B, C synthesized, then A received as a cache hit; after D and the eviction run,
    // A is still cached and B (now least recently used) is not.
    [Fact]
    public async Task TOR_04_rYFhU7O_cache_hit_refreshes_last_used()
    {
        await using var client = await StartWithRoomForThreeClipsAsync();
        await SynthesizeOneAsync(client, "A");
        await SynthesizeOneAsync(client, "B");
        await SynthesizeOneAsync(client, "C");
        var hit = await SynthesizeOneAsync(client, "A");
        Assert.True(hit.GetProperty("cached").GetBoolean());
        await SynthesizeOneAsync(client, "D");

        await RunEvictorAsync(client);

        Assert.True((await SynthesizeOneAsync(client, "A")).GetProperty("cached").GetBoolean());
        Assert.False((await SynthesizeOneAsync(client, "B")).GetProperty("cached").GetBoolean());
    }

    // Eviction deletes the clip's WAV file and its download links with it.
    [Fact]
    public async Task Eviction_removes_the_audio_file_and_its_download_links()
    {
        await using var client = await StartWithRoomForThreeClipsAsync();
        var a = await SynthesizeOneAsync(client, "A");
        var keyA = a.GetProperty("cacheKey").GetString()!;
        var fileA = client.Host.Services.GetRequiredService<AudioDirectory>().FileFor(keyA);
        Assert.True(File.Exists(fileA));
        await SynthesizeOneAsync(client, "B");
        await SynthesizeOneAsync(client, "C");
        await SynthesizeOneAsync(client, "D");

        await RunEvictorAsync(client);

        Assert.False(File.Exists(fileA));
        Assert.Equal(0, await CountAsync(client, "SELECT COUNT(*) FROM clip_downloads WHERE cache_key = $key;", keyA));
        Assert.Equal(3, await CountAsync(client, "SELECT COUNT(*) FROM audio_cache;", null));
        using var download = await client.DownloadAsync(a.GetProperty("url").GetString()!);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    // TOR-08-VHkMu93 (data source of "Evicted this month"): each clip the eviction pass removes is
    // recorded as one cache_evictions row with its key and size.
    [Fact]
    public async Task TOR_08_VHkMu93_each_evicted_clip_is_recorded_in_cache_evictions()
    {
        await using var client = await StartWithRoomForThreeClipsAsync();
        var a = await SynthesizeOneAsync(client, "A");
        var b = await SynthesizeOneAsync(client, "B");
        await SynthesizeOneAsync(client, "C");
        await SynthesizeOneAsync(client, "D");
        await SynthesizeOneAsync(client, "E");
        var keyA = a.GetProperty("cacheKey").GetString()!;
        var keyB = b.GetProperty("cacheKey").GetString()!;
        var ownerA = await OwnerOfAsync(client, "audio_cache", keyA);
        var ownerB = await OwnerOfAsync(client, "audio_cache", keyB);
        Assert.False(string.IsNullOrEmpty(ownerA));

        await RunEvictorAsync(client);

        // Each eviction row carries the subject who created the evicted clip (access rule: owner column).
        Assert.Equal(ownerA, await OwnerOfAsync(client, "cache_evictions", keyA));
        Assert.Equal(ownerB, await OwnerOfAsync(client, "cache_evictions", keyB));
        Assert.Equal(2, await client.Host.CountRowsAsync("cache_evictions"));
        Assert.Equal(1, await CountAsync(client, "SELECT COUNT(*) FROM cache_evictions WHERE cache_key = $key;", a.GetProperty("cacheKey").GetString()));
        Assert.Equal(1, await CountAsync(client, "SELECT COUNT(*) FROM cache_evictions WHERE cache_key = $key;", b.GetProperty("cacheKey").GetString()));
        Assert.Equal(ClipBytes * 2, await CountAsync(client, "SELECT SUM(size_bytes) FROM cache_evictions;", null));
    }

    // Download links expired more than 24 h ago are removed; a link expired only minutes ago still answers 410.
    [Fact]
    public async Task Expired_download_links_are_kept_24_hours_then_removed()
    {
        await using var client = await StartWithRoomForThreeClipsAsync();
        var old = await SynthesizeOneAsync(client, "A");
        client.Clock.Advance(TimeSpan.FromHours(25));
        var recent = await SynthesizeOneAsync(client, "B");
        client.Clock.Advance(TimeSpan.FromMinutes(16));

        await RunEvictorAsync(client);

        Assert.Equal(1, await client.Host.CountRowsAsync("clip_downloads"));
        using var recentDownload = await client.DownloadAsync(recent.GetProperty("url").GetString()!);
        Assert.Equal(HttpStatusCode.Gone, recentDownload.StatusCode);
        using var oldDownload = await client.DownloadAsync(old.GetProperty("url").GetString()!);
        Assert.Equal(HttpStatusCode.NotFound, oldDownload.StatusCode);
    }

    private static async Task<long> SizeOfAsync(NarrationClient client, string cacheKey)
    {
        return await CountAsync(client, "SELECT size_bytes FROM audio_cache WHERE cache_key = $key;", cacheKey);
    }

    private static async Task<string?> OwnerOfAsync(NarrationClient client, string table, string cacheKey)
    {
        var opened = await client.Host.Services.GetRequiredService<SqliteConnectionFactory>().OpenAsync();
        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT created_by_subject FROM {table} WHERE cache_key = $key;";
        command.Parameters.AddWithValue("$key", cacheKey);
        return (string?)await command.ExecuteScalarAsync();
    }

    private static async Task<long> CountAsync(NarrationClient client, string sql, string? cacheKey)
    {
        var opened = await client.Host.Services.GetRequiredService<SqliteConnectionFactory>().OpenAsync();
        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (cacheKey is not null)
        {
            command.Parameters.AddWithValue("$key", cacheKey);
        }

        return (long)(await command.ExecuteScalarAsync())!;
    }
}
