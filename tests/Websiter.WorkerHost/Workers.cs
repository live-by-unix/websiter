using Websiter;

namespace Websiter.WorkerHost;

/// <summary>Test worker: request/response handlers over IPC.</summary>
public class TestWorker : Worker
{
    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        RegisterHandler("Echo", (message, ct) =>
        {
            var msg = message?.GetProperty("msg").GetString() ?? "";
            return Task.FromResult<object?>(new { echo = msg, worker = Name });
        });

        RegisterHandler("Add", (message, ct) =>
        {
            var a = message?.GetProperty("a").GetInt32() ?? 0;
            var b = message?.GetProperty("b").GetInt32() ?? 0;
            return Task.FromResult<object?>(new { sum = a + b });
        });

        await RunAsync(cancellationToken);
    }
}

/// <summary>Worker that exits with a non-zero code after a delay (crash tests).</summary>
public class CrashyWorker : Worker
{
    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1500, cancellationToken);
        Environment.Exit(3);
    }
}

/// <summary>Worker that talks to TestWorker (worker-to-worker IPC).</summary>
public class RelayWorker : Worker
{
    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        RegisterHandler("Relay", async (System.Text.Json.JsonElement? message, CancellationToken ct) =>
        {
            var result = await IPC.RequestAsync<object>("TestWorker", "Echo",
                new { msg = "relayed" }, timeout: TimeSpan.FromSeconds(10), ct: ct);
            return result;
        });

        await RunAsync(cancellationToken);
    }
}
