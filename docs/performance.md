# Performance

## Design choices that matter

- **Async I/O everywhere** — Kestrel, IPC sockets, file serving are all async with `CancellationToken` propagation.
- **Bounded IPC queues** — `ipc:sendQueueCapacity` (default 256) applies backpressure instead of unbounded buffering.
- **Connection reuse** — IPC connections are pooled per worker; requests multiplex over them.
- **Static files** — sent via `SendFileAsync` (kernel sendfile where available) with fingerprinted assets getting long `Cache-Control`.
- **Component caching** — rendered fragments cached by file stamp; invalidated on change.
- **Worker isolation** — CPU-heavy work in workers keeps the HTTP path responsive; scale by adding worker processes.

## Tuning knobs

| Setting | Where | Effect |
|---|---|---|
| `server:port` / `host` | websiter.json | Bind address |
| `workers:restartLimit`, `restartDelayMs` | websiter.json | Restart behavior |
| `ipc:requestTimeoutSeconds`, `maxMessageBytes`, `sendQueueCapacity` | websiter.json / IpcOptions | IPC limits |
| `security:rateLimit:*` | websiter.json | Per-IP throttling |

## Measuring

- `GET /__websiter/metrics` — request counts and latencies by route.
- Structured logs include elapsed ms per request.
- For benchmarks, measure on your target hardware; methodology notes belong in your own repo — Websiter does not publish synthetic numbers.

## What to avoid

- Synchronous `.Result`/`.Wait()` in handlers — they starve the thread pool.
- Unbounded `Task.Run` fan-out per request — use workers + IPC queues.
- Reading whole files into memory for large downloads — `SendFileAsync`/streaming helpers.
