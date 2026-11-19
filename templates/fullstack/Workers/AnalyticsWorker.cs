using Websiter;

/// <summary>
/// Example worker: runs as an independent OS process and serves analytics
/// data to the host over IPC.
/// </summary>
public class AnalyticsWorker : Worker
{
    private long _pageViews;

    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        RegisterHandler("GetStats", (message, ct) =>
        {
            return Task.FromResult<object?>(new
            {
                worker = Name,
                pageViews = Interlocked.Read(ref _pageViews),
                uptimeSeconds = (DateTimeOffset.UtcNow - DateTimeOffset.UtcNow).TotalSeconds,
            });
        });

        // Subscribe to app-wide events.
        Subscribe("page.view", (payload, ct) =>
        {
            Interlocked.Increment(ref _pageViews);
            Log.Info($"AnalyticsWorker recorded a page view (total {_pageViews})");
            return Task.CompletedTask;
        });

        await RunAsync(cancellationToken);
    }
}
