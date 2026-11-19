# IPC

Websiter's built-in inter-process communication. Workers and the host exchange messages over **Unix domain sockets** (Linux/macOS) or **named pipes** (Windows) — never exposed on the network.

## Endpoints

Each participant listens on a namespaced endpoint `{appName}.{participantName}` inside the IPC directory (`.websiter/ipc` or `WEBSITER_IPC_DIR`). Socket files and the directory are chmod `700`/`600` where supported.

If the socket path would exceed the OS limit (~104 chars on macOS), a deterministic hashed short path under the temp dir is used — transparent to applications.

## Request / response

```csharp
// In host or another worker:
var users = await IPC.RequestAsync<UserList>("DatabaseWorker", "GetUsers");
var raw    = await IPC.RequestAsync("DatabaseWorker", "GetUsers");   // IpcEnvelope
```

```csharp
// In DatabaseWorker:
RegisterHandler("GetUsers", async message => await db.GetUsersAsync());
```

- Correlation IDs match requests to responses.
- Errors propagate: a throwing handler produces `IpcException` on the caller with the handler's message.
- Unknown actions return a protocol-level error, not a hang.
- Responses travel back on the **same connection** — no extra socket per reply.

## One-way messages

```csharp
await IPC.SendAsync("EmailWorker", "SendReceipt", receipt);
```

Fire-and-forget; no response is sent.

## Events (pub/sub)

```csharp
// Subscriber (any process):
Subscribe("orders.created", payload => { /* handle */ });

// Publisher:
await IPC.PublishAsync("orders.created", order);
```

`PublishAsync` fans out to every registered subscriber across the app via the IPC registry (`.websiter/ipc/registry/*.json`). Directed events to one worker: `IPC.SendEventAsync(worker, topic, payload)`.

**Delivery semantics: at-least-once.** Subscribers must tolerate duplicates; there is no exactly-once guarantee.

## Timeouts & cancellation

```csharp
await IPC.RequestAsync<T>("Worker", "Action", payload,
    timeout: TimeSpan.FromSeconds(5), ct: cancellationToken);
```

Default timeout: `ipc:requestTimeoutSeconds` (30s). Cancellation aborts the pending request.

## Message limits & backpressure

- `ipc:maxMessageBytes` (default 4 MiB) rejects oversized envelopes.
- Each connection has a bounded outbound queue (`ipc:sendQueueCapacity`, default 256); writers apply backpressure rather than dropping.

## Built-in actions

Every participant answers:

- `$ping` → `{ pong, worker, utc }`
- `$status` → pid, uptime, subscriptions, counters
- `$shutdown` → graceful stop (used by supervisor/CLI)

## Protocol

Versioned JSON envelopes (`IpcEnvelope`): `version`, `kind` (request/send/response/event), `id`, `correlationId`, `from`, `to`, `action`, `payload`, `ok`, `error`. Framing: 4-byte big-endian length prefix + UTF-8 JSON payload.

## Security

- Local-only transports; no TCP listener is created.
- Filesystem permissions restrict endpoints to the owning user.
- No authentication tokens are sent — isolation relies on OS user boundaries. Run workers under separate OS users for stronger isolation.
