namespace PeakMediaRemoteMcpServer.Components;

/// <summary>Where a dashboard page's data load stands.</summary>
public enum LoadState
{
    Loading,
    Cancelled,
    Error,
    Loaded,
}

/// <summary>
/// One dashboard page's data load, with the behavior every data screen shares (UX Baseline: Screen
/// states, Progress feedback): a load in flight shows Loading; after <see cref="CancelOfferedAfter"/> a
/// Cancel is offered; a newer load supersedes an older one, whose result is dropped; a failure is
/// logged and shown as Error. <paramref name="render"/> re-renders the owning component on its
/// dispatcher.
/// </summary>
public sealed class PageLoad<T>(Func<Func<Task>, Task> render, Microsoft.Extensions.Logging.ILogger logger, string what) : IDisposable
    where T : class
{
    /// <summary>After this long a load offers a Cancel button.</summary>
    public static readonly TimeSpan CancelOfferedAfter = TimeSpan.FromSeconds(10);

    private CancellationTokenSource? _load;

    public LoadState State { get; private set; } = LoadState.Loading;

    public T? Value { get; private set; }

    public bool ShowCancel { get; private set; }

    public async Task RunAsync(Func<CancellationToken, Task<Result<T>>> read)
    {
        _load?.Cancel();
        var load = new CancellationTokenSource();
        _load = load;
        State = LoadState.Loading;
        Value = null;
        ShowCancel = false;
        await render(() => Task.CompletedTask);
        _ = OfferCancelLaterAsync(load);

        try
        {
            var result = await read(load.Token);
            if (!ReferenceEquals(_load, load))
            {
                return;
            }

            if (result.IsFailed)
            {
                logger.LogWarning("{What} could not be loaded: {Problem}", what, result.Errors[0].Message);
                State = LoadState.Error;
            }
            else
            {
                Value = result.Value;
                State = LoadState.Loaded;
            }
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested)
        {
            if (ReferenceEquals(_load, load))
            {
                State = LoadState.Cancelled;
            }
        }
        finally
        {
            if (ReferenceEquals(_load, load))
            {
                ShowCancel = false;
                load.Dispose();
                _load = null;
            }
        }

        await render(() => Task.CompletedTask);
    }

    public void Cancel()
    {
        _load?.Cancel();
    }

    private async Task OfferCancelLaterAsync(CancellationTokenSource load)
    {
        try
        {
            await Task.Delay(CancelOfferedAfter, load.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        await render(() =>
        {
            if (ReferenceEquals(_load, load) && State == LoadState.Loading)
            {
                ShowCancel = true;
            }

            return Task.CompletedTask;
        });
    }

    public void Dispose()
    {
        _load?.Cancel();
    }
}
