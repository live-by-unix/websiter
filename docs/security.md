# Security

Security is a core requirement, not an afterthought.

## Defaults

- Security headers on every response (`UseSecurityHeaders`): `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`, and HSTS in production.
- Rate limiting (`UseRateLimiting`): fixed-window per-IP, configurable via `security:rateLimit`.
- CORS (`UseCors`): disabled by default; explicit origins only when enabled.
- Request size limits via Kestrel defaults; JSON body parsing is bounded by `MaxRequestBodySize`.
- Cookies: `HttpOnly`, `Secure`, `SameSite=Lax` defaults in `SetCookie`.
- Error responses are generic JSON (`{ "error": "internal_error" }`) — stack traces go to logs only.

## Secrets

```csharp
var cs = Secrets.ResolveReference(Config.Get("database:connectionString"));
// "secretref:DbPassword" → reads WEBSITER_SECRET_DBPASSWORD
```

- Secrets resolve from environment variables only — never from `websiter.json`.
- `Secrets` never logs values; `Log` redacts nothing automatically, so **never log secrets yourself**.

## IPC security

- Local-only transports; nothing listens on TCP.
- Socket files `600`, IPC directory `700` (best-effort on filesystems supporting unix modes).
- For multi-user hosts, run the app under a dedicated OS user; IPC inherits filesystem permissions.

## Input validation

```csharp
Validation.IsEmail(v); Validation.IsSlug(v); Validation.IsStrongPassword(v);
Validation.IsIpAddress(v); Validation.IsHttpUrl(v);
```

Validate at the boundary (routes/workers), parameterize all queries, and encode output. Components insert HTML verbatim — escape user-controlled data before embedding.

## Transport security

- HTTP→HTTPS redirect + HSTS in production.
- TLS via `server:https` config (certificate paths) or a reverse proxy (see [SSL](ssl.md)).
- Dependency scanning in CI: `dotnet list package --vulnerable --include-transitive`.

See also [SECURITY.md](../SECURITY.md) for vulnerability reporting.
