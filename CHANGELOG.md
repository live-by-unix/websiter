# Changelog

All notable changes to Websiter are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
adheres to [Semantic Versioning](https://semver.org/) once 1.0 is reached.

## [Unreleased]

### Added

- Complete framework: Core, Runtime, Server, Workers, IPC, Routing,
  Components, Frontend, Native, Security, Diagnostics, Deployment, CLI.
- Independent worker OS processes with supervision, restart policies,
  heartbeats, and per-worker logs.
- Native IPC over Unix domain sockets / named pipes: request/response,
  one-way sends, pub/sub events, correlation IDs, timeouts, bounded queues.
- HTTP server on Kestrel: routing with parameters, middleware, static files,
  HTML pages, JSON APIs, WebSockets, TLS, compression, CORS, rate limiting,
  security headers, health/metrics endpoints.
- HTML component engine: nested includes, caching, cycle detection, watching.
- Frontend asset pipeline: hashing, manifests, content types.
- `websiter` CLI: new, dev, build, start, stop, restart, workers, routes,
  logs, doctor, config, clean, deploy, domain, ssl.
- Templates: basic, fullstack, api, distributed.
- Examples: HelloWorld, Dashboard, WorkerCommunication, Authentication,
  FullApplication.
- xUnit test suite including real-process worker/IPC integration tests.

### Fixed

- IPC responses now return on the originating connection so request/response
  correlation resolves correctly.
- Unix socket paths exceeding the OS `sun_path` limit fall back to a
  deterministic hashed short path.
- Explicit routes take precedence over static-file/page fallbacks.
- Route/query parameter conversion handles primitive types correctly.
- Component subdirectory references (`layout/Header`) resolve safely.
- Worker IPC hub binds to the worker identity before user handlers register.
