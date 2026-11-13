namespace PeakMediaRemoteMcpServer.Broker;

/// <summary>Deletes expired sign-in transactions, authorization codes and refresh tokens every 10 minutes.</summary>
public sealed class BrokerJanitor(
    TransactionStore transactions,
    CodeStore codes,
    RefreshTokenStore refreshTokens,
    ILogger<BrokerJanitor> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SweepOnceAsync(stoppingToken);
        }
    }

    /// <summary>One sweep; returns the number of rows deleted. A failed sweep is logged and retried next tick.</summary>
    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken = default)
    {
        var results = new[]
        {
            await transactions.DeleteExpiredAsync(cancellationToken),
            await codes.DeleteExpiredAsync(cancellationToken),
            await refreshTokens.DeleteExpiredAsync(cancellationToken),
        };

        var failed = results.FirstOrDefault(r => r.IsFailed);
        if (failed is not null)
        {
            logger.LogWarning("Expired sign-in records could not be swept: {Problem}. The next sweep retries in 10 minutes.", failed.Errors[0].Message);
        }

        var deleted = results.Where(r => r.IsSuccess).Sum(r => r.Value);
        if (deleted > 0)
        {
            logger.LogDebug("Swept {Count} expired sign-in record(s).", deleted);
        }

        return deleted;
    }
}
