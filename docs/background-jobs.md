# Background Jobs

Long-running or scheduled work belongs in **workers** — that's what they're for.

## Periodic job worker

```csharp
public class ReportWorker : Worker
{
    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        RegisterHandler("RunNow", async _ => { await GenerateReportAsync(); return new { ok = true }; });

        // Periodic loop alongside IPC:
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromHours(1), cancellationToken);
                await GenerateReportAsync();
            }
        }, cancellationToken);

        await RunAsync(cancellationToken);
    }
}
```

The worker keeps its IPC endpoint alive (responding to `$ping`, `RunNow`, etc.) while the background loop runs. Supervisor restarts apply if it crashes.

## Triggering jobs

- From HTTP: `await IPC.SendAsync("ReportWorker", "RunNow")` inside a route handler.
- From another worker: same call — workers talk IPC directly.
- On a schedule: `cron`/`systemd timer` calling `websiter workers restart` is unnecessary — prefer an in-worker timer or an external scheduler hitting an authenticated route that sends the IPC message.

## Queues

For heavy queues, dedicate a worker per queue with bounded channels (`System.Threading.Channels`) inside the worker for backpressure. Scale by adding more worker instances of the same class — each is a separate process pulling work over IPC or a shared broker.
