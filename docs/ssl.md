# SSL/TLS

Two options: terminate at Kestrel, or at a reverse proxy (recommended).

## Option A — Kestrel TLS

`websiter.json`:

```json
{
  "server": {
    "host": "0.0.0.0",
    "port": 8080,
    "https": {
      "enabled": true,
      "port": 8443,
      "certificatePath": "/etc/ssl/mywebsite.crt",
      "keyPath": "/etc/ssl/mywebsite.key"
    }
  }
}
```

Provide a PFX or PEM pair; keep key files `600` and out of the repo.

## Option B — Reverse proxy (recommended)

nginx/Caddy/Traefik terminates TLS and proxies to `http://127.0.0.1:8080`. Get certificates with certbot:

```bash
./scripts/configure-ssl.sh example.com
# = certbot --nginx -d example.com --redirect --agree-tos
```

Auto-renewal: `systemctl status certbot.timer`.

## Hardening

- HTTP→HTTPS redirect at the proxy (or Kestrel when TLS-enabled).
- HSTS is emitted automatically in Production when `UseSecurityHeaders` runs.
- `SetCookie(secure: true)` is the default — cookies won't be sent over plain HTTP.
- Never commit `.pfx`/`.key` files; use `secretref:`-style env vars or file paths outside the repo.
