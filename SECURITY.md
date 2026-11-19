# Security Policy

## Supported versions

Websiter is pre-1.0. Security fixes are applied to the latest release on the default branch.

## Reporting a vulnerability

**Do not open a public issue for security vulnerabilities.**

Report privately to the maintainer:

- GitHub: open a private vulnerability report via [GitHub Security Advisories](https://github.com/live-by-unix/websiter/security/advisories/new) for this repository, or
- Contact the author (live-by-unix) through GitHub with a request for a private channel.

Please include:

- A description of the vulnerability and its impact.
- Steps to reproduce or a proof of concept.
- Affected versions/components.
- Any suggested mitigation.

You can expect an acknowledgment within a reasonable timeframe. We will coordinate disclosure with you before publishing details.

## Security practices in Websiter

- IPC endpoints are local-only (Unix domain sockets / named pipes) with owner-only permissions where supported; IPC is never exposed on the network.
- Secrets are referenced via `secretref:` environment variables and are never logged.
- Security headers, CORS policies, rate limiting, request size limits, and secure cookie defaults are built in.
- Component HTML is inserted verbatim — components are trusted developer markup; user-generated content must be escaped by the application.
- Dependency vulnerabilities are scanned in CI (`dotnet list package --vulnerable`).

## Scope

In scope: the Websiter framework itself (src/), CLI, templates, and worker/IPC runtime.

Out of scope: vulnerabilities in applications built with Websiter, third-party dependencies (report those upstream), and issues requiring physical access to the host.
