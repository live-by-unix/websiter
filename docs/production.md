# Production

## Build

```bash
websiter build --release
```

Produces a Release build with:

- Dependency validation and compile of all workers
- Frontend asset pipeline output (hashed assets + manifest)
- Configuration validation
- Build manifest under `.websiter/`

## Run

```bash
WEBSITER_ENV=Production websiter start
# or directly:
WEBSITER_ENV=Production dotnet run -c Release --no-build
```

## Checklist

- [ ] `websiter.json` reviewed; secrets via `secretref:` env vars only
- [ ] `websiter.Production.json` overrides dev settings (ports, CORS, etc.)
- [ ] HTTPS terminated (Kestrel TLS or reverse proxy) — see [SSL](ssl.md)
- [ ] `workers:autoRestart` + sane `restartLimit` tuned for your SLAs
- [ ] Log rotation: `.websiter/logs/` is plain files — use `logrotate`/journald
- [ ] Health endpoint monitored: `GET /__websiter/health`
- [ ] `.websiter/` excluded from backups/deploys as appropriate (it's runtime state)

## Diagnostics endpoints

| Endpoint | Content |
|---|---|
| `/__websiter/health` | Aggregated health (200/503) |
| `/__websiter/metrics` | Request counters/timings |
| `/__websiter/status` | App + worker status snapshot |
| `/__websiter/routes` | Registered routes |

Restrict these at the reverse proxy if they shouldn't be public.

## Self-contained publish

```bash
dotnet publish -c Release -r linux-x64 --self-contained -o publish/
```

See [Deployment](deployment.md) and [AWS EC2](aws-ec2.md).
