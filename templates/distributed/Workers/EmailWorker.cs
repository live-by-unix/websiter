using Websiter;

/// <summary>
/// Background worker subscribing to events published by other workers.
/// </summary>
public class EmailWorker : Worker
{
    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        Subscribe("auth.login", (payload, ct) =>
        {
            var username = payload?.GetProperty("username").GetString() ?? "user";
            Log.Info($"EmailWorker: queued welcome email for {username}");
            return Task.CompletedTask;
        });

        await RunAsync(cancellationToken);
    }
}
