# Websiter

**Build without boundaries.**
<p align="center">
  <img width="128" height="128" alt="Websiter" src="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'%3E%3Crect width='64' height='64' rx='16' fill='%238c7bff'/%3E%3Cpath d='M8 19h9l6 25 8-19h6l8 19 8-25h9L50 49h-9l-7-17-7 17h-9z' fill='white'/%3E%3C/svg%3E">
</p>

[![CI](https://github.com/live-by-unix/websiter/actions/workflows/ci.yml/badge.svg)](https://github.com/live-by-unix/websiter/actions/workflows/ci.yml)
[![License: BSD-3-Clause](https://img.shields.io/badge/License-BSD--3--Clause-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)

Websiter is a complete, cross-platform, open-source web application framework for C# and .NET. It gives you a first-class HTTP server, reusable HTML components, a modern frontend pipeline, and — its defining feature — **independent worker processes with built-in inter-process communication**.

**Repository:** [github.com/live-by-unix/websiter](https://github.com/live-by-unix/websiter) · Author: [live-by-unix](https://github.com/live-by-unix) · License: [BSD-3-Clause](LICENSE)

---

## Features

- **C# backend on .NET 10 LTS** with ASP.NET Core/Kestrel underneath.
- **Independent worker processes** — each worker is a real OS process with its own memory space, lifecycle, logs, and health monitoring.
- **Native IPC** — request/response, one-way messages, and pub/sub events over Unix domain sockets (Linux/macOS) or named pipes (Windows). No HTTP plumbing required.
- **Routing** — path parameters, query strings, JSON bodies, all HTTP verbs, named routes.
- **HTML components** — reusable `<component name="Navbar" />` fragments with nesting, caching, cycle detection, and file watching.
- **Frontend** — plain HTML5/CSS3/JavaScript, static assets, content-hashed bundles, manifests, live reload in dev.
- **Security** — security headers, CORS, rate limiting, secret references (`secretref:`), input validation, secure cookie defaults.
- **Diagnostics** — structured logs, health endpoints, request metrics, worker status APIs, crash reports.
- **Deployment** — self-contained or framework-dependent publishes, archives, systemd units, AWS EC2 scripts.
- **CLI** — `websiter new`, `dev`, `build`, `start`, `stop`, `workers`, `routes`, `logs`, `doctor`, `deploy`.

## Architecture

```text
┌─────────────────────────────────────────────────────────┐
│                     Your Application                     │
│  Program.cs ── WebsiterApplication ── HTTP routes/pages  │
└───────────────┬───────────────────────┬─────────────────┘
                │ IPC (UDS / pipes)      │ spawns
        ┌───────▼────────┐      ┌───────▼────────┐
        │  Worker A proc │      │  Worker B proc │   ... independent OS processes
        │  AuthWorker    │      │  PaymentWorker │
        └────────────────┘      └────────────────┘
```

Worker processes never share memory with the host or each other. All coordination goes through the IPC layer: envelopes with correlation IDs, bounded send queues for backpressure, timeouts, and cancellation.

## Install

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/live-by-unix/websiter.git
cd websiter

# Install the CLI globally:
./scripts/install-cli.sh       # macOS/Linux — builds and installs the CLI globally
.\scripts\install-cli.ps1      # Windows
```

Or run it straight from the build output:

```bash
dotnet build src/Websiter.CLI -c Release
dotnet src/Websiter.CLI/bin/Release/net10.0/websiter.dll --help
```

Verify:

```bash
websiter --version
websiter --help
```

## Quickstart

```bash
websiter new MyWebsite                 # scaffold from the basic template
cd MyWebsite
websiter dev                          # build, run, watch, restart on change
```

Open http://localhost:8080 — you have a running Websiter app with pages, static assets, and (in richer templates) workers.

### Other templates

```bash
websiter new Shop --template fullstack     # pages + components + workers + IPC
websiter new Api --template api            # JSON REST API
websiter new Cluster --template distributed # multiple coordinated workers
```

## A minimal application

```csharp
using Websiter;

var app = WebsiterApplication.Create(args);

app.MapGet("/", ctx => ctx.Response.WriteAsync("Hello Websiter!"));
app.MapGet("/api/status", ctx =>
    ctx.WriteJsonAsync(new { status = "online", framework = "Websiter" }));
app.MapGet("/users/{id}", ctx =>
    ctx.WriteJsonAsync(new { id = ctx.Param<int>("id") }));

return await app.RunAsync();
```

Pages live in `Pages/*.html`, components in `Components/*.html`, static files in `wwwroot/`. Explicit routes take precedence over page/static fallbacks.

## Workers and IPC

```csharp
public class AuthWorker : Worker
{
    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        RegisterHandler("Authenticate", async message =>
            await AuthenticateAsync(message));

        await RunAsync(cancellationToken);   // binds IPC + starts listening
    }
}
```

Call it from your host or another worker:

```csharp
var user = await IPC.RequestAsync<User>("AuthWorker", "Authenticate", credentials);
await IPC.SendAsync("EmailWorker", "SendWelcome", payload);      // one-way
await IPC.PublishAsync("user.created", payload);                  // pub/sub event
```

Workers are discovered automatically, launched as separate processes, supervised with restart policies (exponential backoff, restart limits), and manageable via `websiter workers list|start|stop|restart|logs`.

## Project layout (generated app)

```text
MyWebsite/
├── websiter.json        # application configuration
├── Program.cs           # routes + startup
├── Workers/             # worker classes (each = own process)
├── Pages/               # HTML pages
├── Components/          # reusable HTML fragments
├── wwwroot/             # static assets (css/js/images)
├── Styles/  Scripts/    # frontend sources
└── .websiter/           # runtime state: logs, ipc, pid files (git-ignored)
```

## CLI reference

| Command | Description |
|---|---|
| `websiter new <name> [--template t]` | Scaffold a new project |
| `websiter dev` | Build, run, watch sources, restart on change |
| `websiter build [--release]` | Production build + asset pipeline |
| `websiter start` / `stop` / `restart` | Manage the running app |
| `websiter workers list\|start\|stop\|restart\|logs` | Worker process management |
| `websiter routes` | List registered HTTP routes |
| `websiter logs [--follow]` | View application logs |
| `websiter doctor` | Environment diagnostics |
| `websiter config` | Show effective configuration |
| `websiter clean` | Remove build/runtime artifacts |
| `websiter deploy [aws]` | Package for deployment |
| `websiter domain add <d>` / `ssl enable` | Domain & TLS assistance |

Run `websiter <command> --help` for details. Non-interactive mode: pass `--non-interactive` or set `CI=1`.

## Configuration

`websiter.json` (merged with `websiter.<Environment>.json` and `WEBSITER_*` env vars):

```json
{
  "application": { "name": "MyWebsite", "environment": "Development" },
  "server":     { "host": "0.0.0.0", "port": 8080 },
  "workers":    { "autoRestart": true, "restartLimit": 5 },
  "ipc":        { "transport": "auto" }
}
```

Secrets: reference environment variables with `"secretref:Name"` — resolved via `Secrets.ResolveReference`, never logged.

## Testing

```bash
dotnet test Websiter.slnx
```

The suite includes unit tests plus **integration tests that launch real worker OS processes** and exchange IPC messages (request/response, events, worker→worker, graceful shutdown, crash exit codes).

## Documentation

See [`docs/`](docs/) — installation, quickstart, architecture, CLI, configuration, routing, components, workers, IPC, security, deployment, and more.

## Examples

- [`examples/HelloWorld`](examples/HelloWorld) — minimal site
- [`examples/Dashboard`](examples/Dashboard) — multi-page site with components
- [`examples/WorkerCommunication`](examples/WorkerCommunication) — workers talking over IPC
- [`examples/Authentication`](examples/Authentication) — auth worker + session flow
- [`examples/FullApplication`](examples/FullApplication) — combined showcase

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Found a bug or have an idea? [Open an issue](https://github.com/live-by-unix/websiter/issues). Security issues: see [SECURITY.md](SECURITY.md).

## License

BSD-3-Clause © live-by-unix. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
