# Troubleshooting

## `websiter dev` doesn't restart on change

- Confirm you're in the app directory (`websiter dev` uses `--root`).
- Check the watcher covers your file types; only `*.cs`, web assets, and config trigger rebuilds.
- Look at `.websiter/logs/app.log` for build errors — the dev runner prints the `dotnet build` output on failure.

## Worker won't start / `workers list` shows failed

```bash
websiter workers logs <Name> --follow
websiter doctor
```

Common causes:

- Compile error in `Workers/*.cs` — run `dotnet build` directly.
- Missing parameterless constructor or non-`public` worker class.
- `DependsOn` names a worker that doesn't exist (fails fast by design).
- IPC endpoint conflict — delete `.websiter/ipc/` after stopping the app.

## IPC request timeouts

- Is the target worker running? `websiter workers list`.
- Default timeout is 30s (`ipc:requestTimeoutSeconds`) — override per call.
- Endpoint file missing → worker crashed before `RunAsync`; check its log.
- `No IPC handler registered for action 'X'` → the worker didn't call `RegisterHandler("X", ...)` before `RunAsync`, or the worker process is an older build — restart it.

## Port already in use

```json
{ "server": { "port": 9090 } }
```

or `WEBSITER_SERVER__PORT=9090 websiter start`.

## 404 on a page that exists

- Pages live in `Pages/` (not `wwwroot/`): `Pages/about.html` → `/about`.
- Explicit routes beat pages — check `websiter routes` for a conflicting pattern.
- URL must not include `.html` unless the file is under `wwwroot/`.

## Component errors

`ComponentException` messages name the component and directory. Missing file → check `Components/<name>.html` exists; circular include → the error prints the include chain.

## Logs

```bash
websiter logs --follow
ls .websiter/logs/
```
