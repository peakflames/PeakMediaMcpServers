namespace PeakMediaRemoteMcpServer.Cache;

/// <summary>Runs <see cref="CacheEvictor"/> every <c>Storage:EvictionIntervalMinutes</c>, starting one interval after startup.</summary>
public sealed class CacheEvictionService(CacheEvictor evictor, IOptions<StorageOptions> storage) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, storage.Value.EvictionIntervalMinutes));
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var report = await evictor.RunOnceAsync(stoppingToken);
                if (report.IsFailed)
                {
                    Log.Warning("Cache eviction did not run: {Problem}. It will be tried again in {Minutes} minutes.", report.Errors[0].Message, interval.TotalMinutes);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
