# CLI Reference

The `websiter` command manages the whole lifecycle of an application.

## Project commands

```bash
websiter new <name> [--template basic|fullstack|api|distributed] [--dir <path>] [--non-interactive]
websiter dev [--root <path>]
websiter build [--release] [--root <path>]
websiter clean [--root <path>]
```

## Process commands

```bash
websiter start [--root <path>]
websiter stop  [--root <path>]
websiter restart [--root <path>]
```

`start` launches the built app detached, records its PID under `.websiter/`, and returns. `stop` sends a graceful shutdown (SIGTERM / IPC `$shutdown`) and falls back to a hard kill after a timeout.

## Worker commands

```bash
websiter workers list [--root <path>]
websiter workers start <WorkerName>
websiter workers stop <WorkerName>
websiter workers restart <WorkerName>
websiter workers logs <WorkerName> [--lines N] [--follow]
```

These talk to the running host over IPC (`$workers`, `$worker-start`, `$worker-stop`, `$worker-restart`), so they work against live processes.

## Diagnostics

```bash
websiter routes [--root <path>]   # list registered HTTP routes
websiter logs   [--follow]        # tail application + worker logs
websiter doctor                   # check SDK, CLI, environment
websiter config                   # print effective configuration
```

## Deployment

```bash
websiter deploy [aws|systemd] [--root <path>]
websiter domain add <domain>
websiter ssl enable [--domain <domain>]
```

## Global flags

- `--root <path>` — app directory (default: current directory)
- `--non-interactive` / `CI=1` — disable prompts (required in CI)
- `--verbose` / `--quiet` — logging verbosity
- `--help` / `--version` on any command

Exit codes: `0` success, `1` general error, `2` usage error.
