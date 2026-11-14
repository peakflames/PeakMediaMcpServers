namespace PeakMediaRemoteMcpServer.Cache;

/// <summary>What one eviction pass removed.</summary>
public sealed record EvictionReport(int ClipsEvicted, long BytesFreed, long BytesRemaining, int DownloadLinksRemoved);

/// <summary>
/// Keeps the shared audio cache within <c>Storage:CacheMaxGb</c>: removes the least-recently-used clips
/// (oldest <c>last_used_utc</c> first; a cache hit refreshes it) with their WAV files until the total
/// size fits. Download links of an evicted clip go with it (<c>ON DELETE CASCADE</c>). Download links
/// that expired more than <see cref="ExpiredLinkRetention"/> ago are removed in the same pass — they are
/// kept that long so an expired link still answers 410 rather than 404.
/// </summary>
public sealed class CacheEvictor(
    SqliteConnectionFactory connections,
    AudioDirectory audio,
    IOptions<StorageOptions> storage,
    TimeProvider clock)
{
    public static readonly TimeSpan ExpiredLinkRetention = TimeSpan.FromHours(24);

    private const double BytesPerGb = 1024d * 1024 * 1024;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The cache limit in bytes.</summary>
    public long MaxBytes => (long)Math.Min(long.MaxValue, storage.Value.CacheMaxGb * BytesPerGb);

    public async Task<Result<EvictionReport>> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var opened = await connections.OpenAsync(cancellationToken);
            if (opened.IsFailed)
            {
                return Result.Fail<EvictionReport>(opened.Errors);
            }

            await using var connection = opened.Value;
            var linksRemoved = await RemoveExpiredLinksAsync(connection, cancellationToken);

            var rows = new List<(string Key, long Size)>();
            await using (var select = connection.CreateCommand())
            {
                select.CommandText = "SELECT cache_key, size_bytes FROM audio_cache ORDER BY last_used_utc, created_utc, rowid;";
                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add((reader.GetString(0), reader.GetInt64(1)));
                }
            }

            var total = rows.Sum(r => r.Size);
            var limit = MaxBytes;
            var evicted = 0;
            long freed = 0;
            foreach (var (key, size) in rows)
            {
                if (total <= limit)
                {
                    break;
                }

                await using (var delete = connection.CreateCommand())
                {
                    delete.CommandText = "DELETE FROM audio_cache WHERE cache_key = $key;";
                    delete.Parameters.AddWithValue("$key", key);
                    await delete.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var record = connection.CreateCommand())
                {
                    record.CommandText = "INSERT INTO cache_evictions (cache_key, at_utc, size_bytes) VALUES ($key, $at, $size);";
                    record.Parameters.AddWithValue("$key", key);
                    record.Parameters.AddWithValue("$at", clock.GetUtcNow().ToUnixTimeMilliseconds());
                    record.Parameters.AddWithValue("$size", size);
                    await record.ExecuteNonQueryAsync(cancellationToken);
                }

                DeleteFile(key);
                total -= size;
                freed += size;
                evicted++;
            }

            if (evicted > 0)
            {
                Log.Information(
                    "Cache eviction removed {Clips} least-recently-used clips ({Freed} bytes); {Remaining} bytes remain (limit {Limit})",
                    evicted, freed, total, limit);
            }

            return Result.Ok(new EvictionReport(evicted, freed, total, linksRemoved));
        }
        catch (SqliteException ex)
        {
            return Result.Fail<EvictionReport>(new Error("cache eviction failed").CausedBy(ex));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<int> RemoveExpiredLinksAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM clip_downloads WHERE expires_utc < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", (clock.GetUtcNow() - ExpiredLinkRetention).ToUnixTimeMilliseconds());
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void DeleteFile(string cacheKey)
    {
        try
        {
            File.Delete(audio.FileFor(cacheKey));
        }
        catch (IOException ex)
        {
            Log.Warning("Evicted clip's audio file could not be deleted: {Kind}", ex.GetType().Name);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Warning("Evicted clip's audio file could not be deleted: {Kind}", ex.GetType().Name);
        }
    }
}
