# Domains

## DNS records

Point your domain at the server:

| Type | Name | Value |
|---|---|---|
| A | `@` | server IPv4 |
| AAAA | `@` | server IPv6 (optional) |
| CNAME | `www` | your domain |

```bash
./scripts/configure-domain.sh example.com 8080
```

prints the records and a ready-to-use nginx server block.

## Reverse proxy

Terminate TLS at nginx/Caddy/Traefik and proxy to Kestrel:

```nginx
server {
    listen 80;
    server_name example.com;
    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

Bind Kestrel to loopback in production: `"server": { "host": "127.0.0.1" }`.

## What Websiter does NOT do

Websiter cannot create DNS records at your registrar — that requires provider credentials and APIs. `websiter domain add <domain>` records intent in config and prints the required records; you (or your DNS provider's tooling/CLI) make the actual change.
