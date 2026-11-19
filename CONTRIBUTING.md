# Contributing to Websiter

Thanks for your interest in contributing. Websiter is BSD-3-Clause licensed and welcomes bug reports, feature proposals, documentation improvements, and code contributions.

## Getting started

1. Fork the repository and clone your fork.
2. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download).
3. Build: `dotnet build Websiter.slnx`
4. Test: `dotnet test Websiter.slnx`

## Project layout

- `src/` — framework libraries (`Websiter.Core`, `.IPC`, `.Workers`, `.Server`, `.Routing`, `.Components`, `.Frontend`, `.Security`, `.Diagnostics`, `.Deployment`, `.Native`, `.Runtime`, `.CLI`)
- `templates/` — project scaffolds used by `websiter new`
- `examples/` — runnable example applications
- `tests/` — xUnit test projects, including real-process integration tests
- `docs/` — documentation
- `scripts/` — build/deploy helper scripts

## Guidelines

- **Real functionality only.** No stubs, TODO placeholders for shipped features, or fake implementations.
- **Cross-platform.** Code must work on Windows, Linux, and macOS. IPC transport differences are abstracted — extend the abstraction rather than branching on OS checks in feature code.
- **Async all the way.** Prefer `async`/`await` and `CancellationToken` propagation.
- **Structured logging.** Use `Websiter.Log` / `ILogger`; never log secrets.
- **Tests.** Add or update tests for behavior changes. Integration tests may launch real processes — keep them deterministic and clean up after themselves.
- **Docs.** Update `docs/` when changing user-facing behavior. Do not document features that do not exist.
- **Style.** Follow existing conventions; keep files focused and avoid giant classes.

## Commits and PRs

- Write clear commit messages explaining *why*.
- Keep PRs focused; describe what changed and how it was verified.
- Ensure `dotnet build` and `dotnet test` pass before submitting.

## Reporting issues

Use GitHub Issues. Include OS, .NET SDK version, reproduction steps, and relevant logs. For security vulnerabilities, follow [SECURITY.md](SECURITY.md) instead of filing a public issue.
