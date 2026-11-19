# Authentication

Websiter provides the primitives; you implement your auth scheme (cookie sessions, JWT bearer, OIDC) in a worker or middleware.

## Recommended architecture

```text
Browser → host route → IPC.RequestAsync<AuthWorker>("ValidateSession", token)
                                   ↓
                          AuthWorker verifies against your store
                          (DB, IdP, cache) and returns claims
```

Keeping verification in a worker gives you centralized session state, easy revocation, and isolation of credential material.

## Cookies

Helpers with secure defaults:

```csharp
ctx.SetCookie("session", token, httpOnly: true, secure: true,
              maxAge: TimeSpan.FromHours(8), sameSite: "Lax");
var token = ctx.Cookie("session");
```

`secure: true` requires HTTPS in production — terminate TLS at Kestrel or a reverse proxy and set `X-Forwarded-Proto` handling accordingly.

## Passwords

`Validation.IsStrongPassword(value)` enforces length ≥ 12 plus complexity. Hash with `Rfc2898DeriveBytes.Pbkdf2` or ASP.NET Core `PasswordHasher<T>` — never roll your own, never log passwords.

## Secrets & tokens

Store signing keys/session secrets in env vars referenced via `secretref:`. Rotate by updating the env var and restarting.

## CSRF

For cookie-authenticated state-changing endpoints, enforce an anti-CSRF token (double-submit cookie or synchronizer token) in middleware. `SameSite=Lax` cookies plus origin checks cover most cases; strict `SameSite=Strict` for sensitive flows.
