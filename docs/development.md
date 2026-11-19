# Development

## `websiter dev`

```bash
websiter dev
```

What it does:

1. `dotnet build` the app.
2. Launch the app process (with workers spawned by the supervisor).
3. Watch source directories (`*.cs`, `Pages/`, `Components/`, `wwwroot/`, `Styles/`, `Scripts/`).
4. On change:
   - **Frontend assets only** → rebuild assets, browser reload (dev injects a reload snippet into HTML responses).
   - **C# / worker code** → rebuild; restart only affected worker processes where possible, otherwise the app.
5. Stream host + worker logs to the terminal with `[worker:Name]` prefixes.

## Logs in dev

- Console: structured, colorized.
- Files: `.websiter/logs/app.log`, `.websiter/logs/worker-<Name>.log`.
- `websiter logs --follow` tails everything.

## Configuration in dev

`websiter.json` + `websiter.Development.json` + `WEBSITER_*` env vars. `WEBSITER_ENV=Development` is set automatically in dev mode.

## Debugging workers

- `websiter workers logs <Name> --follow` — worker's own log file.
- Attach a debugger to the worker PID (`websiter workers list` shows PIDs) or set `Debugger.Launch`/`DOTNET_EnableDiagnostics` as needed.
- Workers are plain .NET processes — `dotnet-dump`, `dotnet-counters`, `dotnet-trace` all work on them.

## State directory

`.websiter/` holds runtime state: `ipc/` sockets + registry, `logs/`, `worker-*.json` heartbeats, pid files, manifests. It's safe to delete when the app is stopped (`websiter clean` does this plus build artifacts).
