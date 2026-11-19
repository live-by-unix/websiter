# Architecture

Websiter is a modular monorepo. Each `src/Websiter.*` project has a single responsibility:

| Project | Responsibility |
|---|---|
| `Websiter.Core` | Logging (`Log`), configuration (`Config`), environment, paths |
| `Websiter.Runtime` | `WebsiterApplication` composition root, host lifecycle |
| `Websiter.Server` | Kestrel host, middleware pipeline, static files, pages |
| `Websiter.Routing` | Route table, pattern matching, route values |
| `Websiter.Workers` | `Worker` base class, discovery, process launcher, supervisor |
| `Websiter.IPC` | Envelopes, framing, transports (UDS/named pipes), hub, registry |
| `Websiter.Components` | HTML component discovery, rendering, caching, watching |
| `Websiter.Frontend` | Asset pipeline: hashing, manifests, content types |
| `Websiter.Native` | Process utilities, project path helpers |
| `Websiter.Security` | Headers, rate limiting, CORS options, secrets, validation |
| `Websiter.Diagnostics` | Health checks, metrics, crash reporting |
| `Websiter.Deployment` | Publish packaging, systemd unit generation |
| `Websiter.CLI` | The `websiter` command-line tool |

## Process model

- **Host process** — runs Kestrel, serves HTTP, supervises workers.
- **Worker processes** — one OS process per `Worker` subclass. No shared memory.
- **IPC** — workers and host communicate over Unix domain sockets (Linux/macOS) or named pipes (Windows) via length-prefixed JSON envelopes.

## Request pipeline

`Kestrel → middleware (exception handler, security headers, CORS, compression, rate limit, static files/pages) → route table → handler`

Explicitly mapped routes take precedence over static files and `Pages/` fallbacks.

## Worker lifecycle

`discovered → spawned → starting → running ⇄ restarting → stopping → stopped / failed / crashed`

The supervisor applies restart policies: exponential backoff, max restarts, startup timeouts, and heartbeat monitoring. See [Workers](workers.md).
