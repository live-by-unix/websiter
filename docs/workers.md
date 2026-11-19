# Workers

Workers are **independent operating-system processes** — the defining feature of Websiter. Each `Worker` subclass runs in its own process with its own memory space; a crash never takes down the host or sibling workers.

## Writing a worker

`Workers/AuthWorker.cs`:

```csharp
using Websiter;

public class AuthWorker : Worker
{
    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        RegisterHandler("Authenticate", async message =>
            await AuthenticateAsync(message));

        await RunAsync(cancellationToken);   // binds IPC, starts listening, blocks
    }

    private Task<object?> AuthenticateAsync(JsonElement? message) { /* ... */ }
}
```

That's it — `websiter dev`/`build`/`start` discovers `Workers/*.cs`, compiles them into your app, and launches each as a separate process.

## Lifecycle states

`starting → running → (restarting) → stopping → stopped` · failure paths: `failed`, `crashed`

The supervisor tracks PID, exit codes, heartbeats, restart counts, and per-worker logs under `.websiter/`.

## Restart policies

From `websiter.json` → `workers`:

```json
{
  "workers": {
    "autoRestart": true,
    "restartLimit": 5,
    "restartDelayMs": 1000,
    "maxRestartDelayMs": 30000,
    "startupTimeoutMs": 30000
  }
}
```

- Exponential backoff between `restartDelayMs` and `maxRestartDelayMs`.
- After `restartLimit` rapid failures the worker is marked `failed` (manual restart via CLI).
- `startupTimeoutMs` bounds how long a worker may take to open its IPC endpoint.

## Dependencies & ordering

```csharp
public override IReadOnlyList<string> DependsOn => new[] { "DatabaseWorker" };
```

Dependencies start first; unknown dependencies fail fast at supervisor startup.

## Managing workers

```bash
websiter workers list
websiter workers restart AuthWorker
websiter workers logs AuthWorker --follow
websiter workers stop AuthWorker
```

Or over HTTP on a running app: `GET /__websiter/status` includes worker states; IPC `$workers`, `$worker-start`, `$worker-stop`, `$worker-restart` drive the CLI.

## Worker → worker communication

Workers call each other directly through `IPC` — see [IPC](ipc.md).
