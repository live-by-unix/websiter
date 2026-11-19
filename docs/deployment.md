# Deployment

Websiter apps are ordinary .NET applications — deploy them anywhere .NET runs.

## Package types

| Type | Command | Result |
|---|---|---|
| Framework-dependent | `dotnet publish -c Release` | Small; needs .NET 10 runtime on host |
| Self-contained | `dotnet publish -c Release -r <rid> --self-contained` | Single-folder app, no runtime needed |
| RID-specific | `-r linux-x64`, `linux-arm64`, `osx-arm64`, `win-x64` | Platform-optimized |

## `websiter deploy`

```bash
websiter deploy            # produces publish/ + manifest + (on Linux) a systemd unit
websiter deploy aws        # EC2-oriented packaging + guidance (see aws-ec2.md)
websiter deploy systemd    # emit/install a systemd service unit
```

`Websiter.Deployment` generates:

- Publish output directories per RID
- `websiter.service` systemd unit (user or system service)
- Environment file template (`EnvironmentFile=`) for secrets
- A deployment manifest with checksums

## systemd example

```ini
[Unit]
Description=MyWebsite (Websiter)
After=network.target

[Service]
WorkingDirectory=/opt/mywebsite
ExecStart=/opt/mywebsite/MyWebsite
Environment=WEBSITER_ENV=Production
EnvironmentFile=/etc/mywebsite/env
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

`websiter deploy systemd` writes this for you with correct paths.

## Reverse proxy

Run Kestrel on `127.0.0.1:8080` behind nginx/Caddy/Traefik for TLS, static caching, and domain routing. See [Domains](domains.md) and [SSL](ssl.md).

## Rollback

Keep versioned publish directories (`releases/<version>/`) with a `current` symlink; rollback = repoint symlink + `systemctl restart`. The deploy scripts don't delete old releases automatically.
